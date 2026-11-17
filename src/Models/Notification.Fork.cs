using System;

namespace SourceGit.Models
{
    /// <summary>
    ///     A notification that offers one thing to do about it -- "Undo", typically.
    ///
    ///     Upstream's notifications only report. The history filters needed more: when a
    ///     gesture removes filters the person did not point at, saying so is half the answer,
    ///     and putting them back is the other half.
    /// </summary>
    public partial class Notification
    {
        public string ActionText { get; set; } = string.Empty;

        public Action Action { get; set; } = null;

        public bool HasAction
        {
            get => Action != null && !string.IsNullOrEmpty(ActionText);
        }

        public static void Send(string group, string message, string actionText, Action action)
        {
            Raised?.Invoke(new Notification
            {
                Group = group,
                Message = message,
                ActionText = actionText,
                Action = action,
            });
        }
    }
}
