using AutoClaude.App.ViewModels.SessionEvents;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AutoClaude.App.Views.SessionEvents;

public sealed class SessionEventTemplateSelector : DataTemplateSelector
{
    public DataTemplate? PhaseStartedTemplate { get; set; }
    public DataTemplate? PhaseCompletedTemplate { get; set; }
    public DataTemplate? TaskStartedTemplate { get; set; }
    public DataTemplate? SubtaskStartedTemplate { get; set; }
    public DataTemplate? ExecutionTemplate { get; set; }
    public DataTemplate? RetryTemplate { get; set; }
    public DataTemplate? InterpretingIntentTemplate { get; set; }
    public DataTemplate? InfoTemplate { get; set; }
    public DataTemplate? UserQuestionTemplate { get; set; }
    public DataTemplate? UserDecisionTemplate { get; set; }
    public DataTemplate? UserConfirmationTemplate { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item)
    {
        return item switch
        {
            PhaseStartedEventViewModel => PhaseStartedTemplate,
            PhaseCompletedEventViewModel => PhaseCompletedTemplate,
            TaskStartedEventViewModel => TaskStartedTemplate,
            SubtaskStartedEventViewModel => SubtaskStartedTemplate,
            ExecutionEventViewModel => ExecutionTemplate,
            RetryEventViewModel => RetryTemplate,
            InterpretingIntentEventViewModel => InterpretingIntentTemplate,
            InfoEventViewModel => InfoTemplate,
            UserQuestionEventViewModel => UserQuestionTemplate,
            UserDecisionEventViewModel => UserDecisionTemplate,
            UserConfirmationEventViewModel => UserConfirmationTemplate,
            _ => InfoTemplate
        };
    }

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container)
        => SelectTemplateCore(item);
}
