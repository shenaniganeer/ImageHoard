using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ImageHoard.App;

internal sealed class BrowserTreeItemTemplateSelector : DataTemplateSelector
{
    public DataTemplate? FolderTemplate { get; set; }
    public DataTemplate? FileTemplate { get; set; }

    protected override DataTemplate SelectTemplateCore(object item)
    {
        var payload = item is TreeViewNode node ? node.Content : item;
        return payload switch
        {
            FolderTreeEntry => FolderTemplate ?? base.SelectTemplateCore(item),
            ImageRow => FileTemplate ?? base.SelectTemplateCore(item),
            _ => base.SelectTemplateCore(item),
        };
    }
}
