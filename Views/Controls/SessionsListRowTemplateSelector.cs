using CrownRFEP_Reader.ViewModels;

namespace CrownRFEP_Reader.Views.Controls;

public class SessionsListRowTemplateSelector : DataTemplateSelector
{
    public DataTemplate? HeaderTemplate { get; set; }
    public DataTemplate? SessionTemplate { get; set; }

    protected override DataTemplate? OnSelectTemplate(object item, BindableObject container)
    {
        return item switch
        {
            SessionGroupHeaderRow => HeaderTemplate,
            SessionRow => SessionTemplate,
            _ => SessionTemplate
        };
    }
}
