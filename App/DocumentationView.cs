using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace EasyHotspot;

// Read only bundled documents. Page the text so a large runtime notice does not
// force the UI thread to lay out hundreds of thousands of characters at once.
public sealed class DocumentationView:StackPanel
{
    const int PageLength=12000;
    readonly ComboBox documents=new(){HorizontalAlignment=HorizontalAlignment.Stretch};
    readonly ProgressRing progress=new(){Width=24,Height=24,IsActive=false,Visibility=Visibility.Collapsed};
    readonly TextBlock text=new(){TextWrapping=TextWrapping.Wrap,IsTextSelectionEnabled=true,FlowDirection=FlowDirection.LeftToRight};
    readonly TextBlock position=new(){VerticalAlignment=VerticalAlignment.Center};
    readonly Button previous=new(),next=new();
    readonly string directory=AppContext.BaseDirectory;
    string[] chunks=[];int current,generation;bool initialized;
    public bool IsLoading=>progress.IsActive;
    public string SelectedDocument=>(documents.SelectedItem as ComboBoxItem)?.Tag as string??"";
    public string DocumentText=>text.Text;
    public Task LoadTask {get;private set;}=Task.CompletedTask;
    static string L(string key)=>Localization.Get(key);

    public DocumentationView()
    {
        Spacing=12;documents.Header=L("DocumentLabel");previous.Content=L("PreviousPage");next.Content=L("NextPage");
        AutomationProperties.SetAutomationId(documents,"DocumentationSelector");
        AutomationProperties.SetAutomationId(text,"DocumentationText");
        AutomationProperties.SetAutomationId(progress,"DocumentationProgress");
        AutomationProperties.SetAutomationId(previous,"DocumentationPrevious");AutomationProperties.SetAutomationId(next,"DocumentationNext");
        var pager=new ActionPanel();pager.Children.Add(previous);pager.Children.Add(position);pager.Children.Add(next);
        Children.Add(documents);Children.Add(progress);Children.Add(pager);Children.Add(text);
        previous.Click+=(_,_)=>{if(current>0){current--;Render();}};
        next.Click+=(_,_)=>{if(current+1<chunks.Length){current++;Render();}};
        documents.SelectionChanged+=(_,_)=>{if(initialized)LoadTask=LoadDocumentAsync();};
        Loaded+=(_,_)=>{if(!initialized)LoadTask=InitializeAsync();};
        Render();
    }

    async Task InitializeAsync()
    {
        if(initialized)return;initialized=true;SetLoading(true);
        try {
            var files=await Task.Run(()=>{
                var paths=new List<string>{"README.zh-CN.md","README.md","README-zh-CN.txt","LICENSE","THIRD-PARTY-NOTICES.md"};
                var notices=Path.Combine(directory,"ThirdParty");
                if(Directory.Exists(notices))paths.AddRange(Directory.EnumerateFiles(notices,"*",SearchOption.AllDirectories)
                    .Where(x=>!x.EndsWith(".tar.gz",StringComparison.OrdinalIgnoreCase))
                    .Select(x=>Path.GetRelativePath(directory,x)).Order(StringComparer.OrdinalIgnoreCase));
                return paths.Where(x=>File.Exists(Path.Combine(directory,x))).ToArray();
            });
            foreach(var file in files)documents.Items.Add(new ComboBoxItem{Content=file.Replace('\\','/'),Tag=file});
            var preferred=Localization.CurrentLanguage.StartsWith("zh",StringComparison.OrdinalIgnoreCase)?"README.zh-CN.md":"README.md";
            documents.SelectedIndex=Math.Max(0,Array.IndexOf(files,preferred));
            // SelectionChanged starts the asynchronous read; await it for callers.
            await LoadTask;
            if(files.Length==0)throw new FileNotFoundException();
        }catch(Exception){text.Text=L("DocumentLoadError");SetLoading(false);}
    }

    async Task LoadDocumentAsync()
    {
        var relative=SelectedDocument;if(relative.Length==0)return;
        var request=++generation;SetLoading(true);text.Text="";chunks=[];current=0;Render();
        try {
            var value=await File.ReadAllTextAsync(Path.Combine(directory,relative));
            var pages=await Task.Run(()=>Split(value));
            if(request!=generation)return;chunks=pages;Render();
        }catch(Exception){if(request==generation)text.Text=L("DocumentLoadError");}
        finally{if(request==generation)SetLoading(false);}
    }

    static string[] Split(string value)
    {
        var pages=new List<string>();
        for(int start=0;start<value.Length;){
            int length=Math.Min(PageLength,value.Length-start);
            if(start+length<value.Length&&char.IsHighSurrogate(value[start+length-1]))length--;
            pages.Add(value.Substring(start,length));start+=length;
        }
        return pages.Count==0?[""]:pages.ToArray();
    }
    void SetLoading(bool value){progress.IsActive=value;progress.Visibility=value?Visibility.Visible:Visibility.Collapsed;previous.IsEnabled=!value&&current>0;next.IsEnabled=!value&&current+1<chunks.Length;}
    void Render(){text.Text=chunks.Length>0?chunks[current]:"";position.Text=chunks.Length>0?Localization.Format("DocumentPage",current+1,chunks.Length):"";SetLoading(IsLoading);}
}
