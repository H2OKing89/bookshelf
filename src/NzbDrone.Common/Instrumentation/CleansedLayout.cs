using System;
using NLog;
using NLog.Layouts;

namespace NzbDrone.Common.Instrumentation
{
    public class CleansedLayout : Layout
    {
        public CleansedLayout(Layout innerLayout)
        {
            InnerLayout = innerLayout ?? throw new ArgumentNullException(nameof(innerLayout));
        }

        public Layout InnerLayout { get; }

        protected override string GetFormattedMessage(LogEventInfo logEvent)
        {
            return CleanseLogMessage.Cleanse(logEvent, InnerLayout.Render);
        }
    }
}
