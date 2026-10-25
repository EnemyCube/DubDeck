using System.IO;
using System.Windows;
using System.Windows.Input;
namespace VideoSync.App;

public partial class MainWindow
{
    private void SetImportBusy(bool busy,int count=0)
    {
        if(busy) CloseFloatingControls();
        _importing=busy;
        UpdateEmptyTimeline();
        ImportButton.IsEnabled=!busy && !_closing;
        foreach(var panel in new FrameworkElement[]{Header,Transport,TimelinePanel})
        {
            panel.IsEnabled=!busy && !_closing;
            panel.Opacity=busy ? .4 : 1;
        }
        Root.Cursor=busy ? Cursors.Wait : null;
        ImportOverlay.Visibility=busy ? Visibility.Visible : Visibility.Collapsed;
        if(busy)
        {
            ImportHeading.Text="Loading files…";
            ImportDetail.Text=$"Preparing to import {count} {(count==1 ? "file" : "files")}…";
            ImportDetail.ToolTip=ImportDetail.Text;
        }
    }
    private void ShowImportFile(int index,int total,string path)
    {
        ImportDetail.Text=$"File {index} of {total} — {Path.GetFileName(path)}";
        ImportDetail.ToolTip=path;
        StatusText.Text=ImportDetail.Text;
    }
}
