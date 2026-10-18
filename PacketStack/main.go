// PacketStack terminates captured IP traffic in gVisor. It creates no adapter,
// routes or firewall rules; the Windows service owns capture and reinjection.
package main

import (
	"bufio"
	"context"
	"encoding/binary"
	"encoding/json"
	"fmt"
	"io"
	"os"

	"github.com/xjasonlyu/tun2socks/v2/core"
	"github.com/xjasonlyu/tun2socks/v2/log"
	"github.com/xjasonlyu/tun2socks/v2/proxy"
	"github.com/xjasonlyu/tun2socks/v2/tunnel"
	"github.com/xjasonlyu/tun2socks/v2/tunnel/statistic"
	"gvisor.dev/gvisor/pkg/buffer"
	"gvisor.dev/gvisor/pkg/tcpip/header"
	"gvisor.dev/gvisor/pkg/tcpip/link/channel"
	"gvisor.dev/gvisor/pkg/tcpip/stack"
)

type config struct {
	Address  string
	Username string
	Password string
}

func run(input io.Reader, output io.Writer, ready func()) error {
	reader := bufio.NewReader(input)
	line, err := reader.ReadString('\n')
	if err != nil || len(line) > 4096 {
		return fmt.Errorf("invalid startup frame: %v", err)
	}
	var cfg config
	if err = json.Unmarshal([]byte(line), &cfg); err != nil {
		return err
	}
	dialer, err := proxy.NewSocks5(cfg.Address, cfg.Username, cfg.Password)
	if err != nil {
		return err
	}
	log.SetLogger(log.Must(log.NewLeveled(log.ErrorLevel)))
	t := tunnel.New(dialer, statistic.DefaultManager)
	t.ProcessAsync()
	defer t.Close()
	link := channel.New(1024, 1500, "")
	s, err := core.CreateStack(&core.Config{LinkEndpoint: link, TransportHandler: t})
	if err != nil {
		return err
	}
	defer s.Close()
	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()
	writeErrors := make(chan error, 1)
	go func() {
		for {
			packet := link.ReadContext(ctx)
			if packet == nil {
				return
			}
			view := packet.ToView()
			data := view.AsSlice()
			err := writeFrame(output, data)
			view.Release()
			packet.DecRef()
			if err != nil {
				writeErrors <- err
				return
			}
		}
	}()
	ready()
	for {
		select {
		case err := <-writeErrors:
			return err
		default:
		}
		packet, err := readFrame(reader)
		if err == io.EOF {
			return nil // Parent exited or stopped the hotspot.
		}
		if err != nil {
			return err
		}
		var protocol = header.IPv4ProtocolNumber
		if packet[0]>>4 == 6 {
			protocol = header.IPv6ProtocolNumber
		} else if packet[0]>>4 != 4 {
			return fmt.Errorf("invalid IP version")
		}
		pkt := stack.NewPacketBuffer(stack.PacketBufferOptions{Payload: buffer.MakeWithData(packet)})
		link.InjectInbound(protocol, pkt)
		pkt.DecRef()
	}
}

func readFrame(r io.Reader) ([]byte, error) {
	var size [2]byte
	if _, err := io.ReadFull(r, size[:]); err != nil {
		return nil, err
	}
	n := int(binary.BigEndian.Uint16(size[:]))
	if n < 20 {
		return nil, fmt.Errorf("invalid packet length: %d", n)
	}
	packet := make([]byte, n)
	_, err := io.ReadFull(r, packet)
	return packet, err
}

func writeFrame(w io.Writer, packet []byte) error {
	if len(packet) < 20 || len(packet) > 65535 {
		return fmt.Errorf("invalid response length: %d", len(packet))
	}
	var size [2]byte
	binary.BigEndian.PutUint16(size[:], uint16(len(packet)))
	if _, err := w.Write(size[:]); err != nil {
		return err
	}
	_, err := w.Write(packet)
	return err
}

func main() {
	if err := run(os.Stdin, os.Stdout, func() { fmt.Fprintln(os.Stderr, "READY") }); err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(1)
	}
}
