using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using WinRT.Interop;
using EasyHotspot.Core;
using Microsoft.UI.Dispatching;

namespace EasyHotspot;

public sealed class TrayIcon : IDisposable
{
    const uint Callback=0x8001;
    readonly IntPtr window,icon;
    readonly SubclassProc callback;
    readonly Action show,exit;
    readonly uint taskbarCreated;
    readonly TrayRegistration registration;
    readonly DispatcherQueueTimer retry;
    bool disposed,checking,restartPending;
    public bool IsAdded=>registration.IsAdded;
    public bool SessionEnding{get;private set;}
    public Task Disposal{get;private set;}=Task.CompletedTask;
    public TrayIcon(Window owner,Action show,Action exit)
    {
        this.show=show;this.exit=exit;window=WindowNative.GetWindowHandle(owner);callback=WindowProc;
        icon=LoadImage(IntPtr.Zero,Path.Combine(AppContext.BaseDirectory,"Assets","app.ico"),1,0,0,0x10|0x40);
        if(icon==IntPtr.Zero)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        taskbarCreated=RegisterWindowMessage("TaskbarCreated");
        if(taskbarCreated==0 || !SetWindowSubclass(window,callback,1,0)){DestroyIcon(icon);throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());}
        // Explorer normally runs at medium integrity, including when this app
        // was launched elevated. Permit only these two shell notifications.
        ChangeWindowMessageFilterEx(window,taskbarCreated,1,IntPtr.Zero);
        ChangeWindowMessageFilterEx(window,Callback,1,IntPtr.Zero);
        registration=new(command=>Task.Run(()=>{var data=Data();if(command==TrayCommand.SetVersion)data.Version=4;return Shell_NotifyIcon((uint)command,ref data);}));
        retry=owner.DispatcherQueue.CreateTimer();retry.IsRepeating=false;retry.Interval=TimeSpan.FromSeconds(1);retry.Tick+=async(_,_)=>await CheckAsync();
        _ = CheckAsync();
    }
    NotifyIconData Data()=>new(){Size=(uint)Marshal.SizeOf<NotifyIconData>(),Window=window,Id=1,Flags=0x87,Message=Callback,Icon=icon,Tip="EasyHotspot",Info="",InfoTitle=""};
    public async Task<bool> EnsureAsync()=>!disposed && await registration.EnsureAsync();
    async Task CheckAsync()
    {
        if(disposed || checking)return;checking=true;
        try {
            do {bool restart=restartPending;restartPending=false;await registration.EnsureAsync(restart);}while(restartPending && !disposed);
        }catch(Exception ex){System.Diagnostics.Debug.WriteLine("Tray registration: "+ex.Message);}
        finally {checking=false;if(!disposed){retry.Interval=TimeSpan.FromSeconds(IsAdded?30:1);retry.Start();}}
    }
    IntPtr WindowProc(IntPtr hwnd,uint message,IntPtr wParam,IntPtr lParam,nuint id,nuint data)
    {
        if(message==0x11)SessionEnding=true;
        else if(message==0x16)SessionEnding=wParam!=IntPtr.Zero;
        if(message==taskbarCreated){restartPending=true;_ = CheckAsync();return IntPtr.Zero;}
        if(message==Callback) {
            uint notification=(uint)lParam.ToInt64()&0xffff;
            if(notification is 0x400 or 0x401 or 0x203)show();
            else if(notification is 0x7b or 0x205)Menu();
            return IntPtr.Zero;
        }
        return DefSubclassProc(hwnd,message,wParam,lParam);
    }
    void Menu()
    {
        var menu=CreatePopupMenu();if(menu==IntPtr.Zero)return;
        try {
            AppendMenu(menu,0,1,Localization.Get("TrayShow"));AppendMenu(menu,0x800,0,"");AppendMenu(menu,0,2,Localization.Get("TrayExit"));
            GetCursorPos(out var point);SetForegroundWindow(window);
            int selected=TrackPopupMenu(menu,0x100|0x2,point.X,point.Y,0,window,IntPtr.Zero);
            if(selected==1)show();else if(selected==2)exit();
            PostMessage(window,0,IntPtr.Zero,IntPtr.Zero);
        }finally{DestroyMenu(menu);}
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;retry.Stop();RemoveWindowSubclass(window,callback,1);Disposal=RemoveAsync();
    }
    async Task RemoveAsync(){try{await registration.DisposeAsync();}finally{DestroyIcon(icon);}}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]internal struct NotifyIconData {
        public uint Size;public IntPtr Window;public uint Id,Flags,Message;public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Tip;
        public uint State,StateMask;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)]public string Info;
        public uint Version;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=64)]public string InfoTitle;
        public uint InfoFlags;public Guid Guid;public IntPtr BalloonIcon;
    }
    [StructLayout(LayoutKind.Sequential)]struct Point{public int X,Y;}
    delegate IntPtr SubclassProc(IntPtr hwnd,uint message,IntPtr wParam,IntPtr lParam,nuint id,nuint data);
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)][return:MarshalAs(UnmanagedType.Bool)]static extern bool Shell_NotifyIcon(uint message,ref NotifyIconData data);
    [DllImport("comctl32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]static extern bool SetWindowSubclass(IntPtr hwnd,SubclassProc callback,nuint id,nuint data);
    [DllImport("comctl32.dll")][return:MarshalAs(UnmanagedType.Bool)]static extern bool RemoveWindowSubclass(IntPtr hwnd,SubclassProc callback,nuint id);
    [DllImport("comctl32.dll")]static extern IntPtr DefSubclassProc(IntPtr hwnd,uint message,IntPtr wParam,IntPtr lParam);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr LoadImage(IntPtr instance,string name,uint type,int width,int height,uint flags);
    [DllImport("user32.dll")]static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]static extern bool ChangeWindowMessageFilterEx(IntPtr window,uint message,uint action,IntPtr change);
    [DllImport("user32.dll")]static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern bool AppendMenu(IntPtr menu,uint flags,nuint id,string text);
    [DllImport("user32.dll")]static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")]static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")]static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")]static extern int TrackPopupMenu(IntPtr menu,uint flags,int x,int y,int reserved,IntPtr window,IntPtr rectangle);
    [DllImport("user32.dll")]static extern bool PostMessage(IntPtr window,uint message,IntPtr wParam,IntPtr lParam);
}
