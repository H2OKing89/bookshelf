using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.DecisionEngine;

namespace NzbDrone.Core.Download.TrackedDownloads
{
    public class TrackedDownloadStatusMessage
    {
        private List<Rejection> _rejections;

        public string Title { get; set; }
        public List<string> Messages { get; set; }
        [Newtonsoft.Json.JsonProperty(ObjectCreationHandling = Newtonsoft.Json.ObjectCreationHandling.Replace)]
        public List<Rejection> Rejections
        {
            get => _rejections ?? (Messages ?? new List<string>()).Select(message => new Rejection(message)).ToList();
            set => _rejections = value;
        }

        public TrackedDownloadStatusMessage(string title, List<string> messages)
        {
            Title = title;
            Messages = messages;
            Rejections = messages.Select(message => new Rejection(message)).ToList();
        }

        public TrackedDownloadStatusMessage(string title, string message)
        {
            Title = title;
            Messages = new List<string> { message };
            Rejections = new List<Rejection> { new Rejection(message) };
        }

        public TrackedDownloadStatusMessage(string title, List<string> messages, IEnumerable<Rejection> rejections)
        {
            Title = title;
            Messages = messages;
            Rejections = rejections.ToList();
        }

        //Constructor for use when deserializing JSON
        public TrackedDownloadStatusMessage()
        {
        }
    }
}
