using System;
using System.Linq;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Notifications.Webhook;
using Readarr.Http;

namespace Readarr.Api.V1.Notifications
{
    [V1ApiController]
    public class NotificationController : ProviderControllerBase<NotificationResource, NotificationBulkResource, INotification, NotificationDefinition>
    {
        public static readonly NotificationResourceMapper ResourceMapper = new ();
        public static readonly NotificationBulkResourceMapper BulkResourceMapper = new ();

        public NotificationController(NotificationFactory notificationFactory)
            : base(notificationFactory, "notification", ResourceMapper, BulkResourceMapper)
        {
        }

        public override ActionResult<NotificationResource> UpdateProvider([FromBody] NotificationResource providerResource, [FromQuery] bool forceSave = false)
        {
            // Existing v1 clients may not yet know the new trigger. Omission must
            // not disable a saved recovery-only connection on an unrelated edit.
            providerResource.OnHealthRestored ??= GetResourceById(providerResource.Id).OnHealthRestored;

            const string completionFieldName = "onImportComplete";
            if (providerResource.Implementation == nameof(Webhook) && providerResource.Fields != null &&
                providerResource.Fields.All(field => field.Name != completionFieldName))
            {
                var previous = GetResourceById(providerResource.Id);
                var completionField = previous.Fields.FirstOrDefault(field => field.Name == completionFieldName);
                if (completionField != null)
                {
                    completionField.Value = JsonSerializer.SerializeToElement(completionField.Value);
                    providerResource.Fields.Add(completionField);
                }
            }

            return base.UpdateProvider(providerResource, forceSave);
        }

        [NonAction]
        public override ActionResult<NotificationResource> UpdateProvider([FromBody] NotificationBulkResource providerResource)
        {
            throw new NotImplementedException();
        }

        [NonAction]
        public override object DeleteProviders([FromBody] NotificationBulkResource resource)
        {
            throw new NotImplementedException();
        }
    }
}
