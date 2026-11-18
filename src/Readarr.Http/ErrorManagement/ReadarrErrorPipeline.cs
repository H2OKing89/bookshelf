using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Net;
using System.Threading.Tasks;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using NLog;
using Npgsql;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Exceptions;
using Readarr.Http.Exceptions;

namespace Readarr.Http.ErrorManagement
{
    public class ReadarrErrorPipeline
    {
        private readonly Logger _logger;

        public ReadarrErrorPipeline(Logger logger)
        {
            _logger = logger;
        }

        public async Task HandleException(HttpContext context)
        {
            _logger.Trace("Handling Exception");

            var response = context.Response;
            var exceptionHandlerPathFeature = context.Features.Get<IExceptionHandlerPathFeature>();
            var exception = exceptionHandlerPathFeature?.Error;

            if (ContainsPostgresFailure(exception))
            {
                // A provider exception may contain a role, database, certificate
                // path or connection-string value even when its password is hidden.
                // Do not render the original exception for either HTTP or logging.
                const string message = "PostgreSQL database request failed.";
                _logger.Error(message);
                await new ErrorModel { Message = message, Description = message }
                    .WriteToResponse(response, HttpStatusCode.InternalServerError);
                return;
            }

            var statusCode = HttpStatusCode.InternalServerError;
            var errorModel = new ErrorModel
            {
                Message = exception?.Message,
                Description = exception?.ToString()
            };

            if (exception is ApiException apiException)
            {
                _logger.Warn(apiException, "API Error:\n{0}", apiException.Message);

                errorModel = new ErrorModel(apiException);
                statusCode = apiException.StatusCode;
            }
            else if (exception is ValidationException validationException)
            {
                _logger.Warn("Invalid request {0}", validationException.Message);

                response.StatusCode = (int)HttpStatusCode.BadRequest;
                response.ContentType = "application/json";
                await response.WriteAsync(STJson.ToJson(validationException.Errors));
                return;
            }
            else if (exception is NzbDroneClientException clientException)
            {
                statusCode = clientException.StatusCode;
            }
            else if (exception is ModelNotFoundException)
            {
                statusCode = HttpStatusCode.NotFound;
            }
            else if (exception is ModelConflictException)
            {
                statusCode = HttpStatusCode.Conflict;
            }
            else if (exception is SQLiteException sqLiteException)
            {
                if (context.Request.Method == "PUT" || context.Request.Method == "POST")
                {
                    if (sqLiteException.Message.Contains("constraint failed"))
                    {
                        statusCode = HttpStatusCode.Conflict;
                    }
                }

                _logger.Error(sqLiteException, "[{0} {1}]", context.Request.Method, context.Request.Path);
            }
            else
            {
                _logger.Fatal(exception, "Request Failed. {0} {1}", context.Request.Method, context.Request.Path);
            }

            await errorModel.WriteToResponse(response, statusCode);
        }

        private static bool ContainsPostgresFailure(Exception exception)
        {
            if (exception == null)
            {
                return false;
            }

            const int maximumExceptions = 64;
            var pending = new Stack<Exception>();
            pending.Push(exception);
            for (var visited = 0; visited < maximumExceptions && pending.Count > 0; visited++)
            {
                var current = pending.Pop();
                if (current is NpgsqlException)
                {
                    return true;
                }

                if (current is AggregateException aggregate)
                {
                    if (aggregate.InnerExceptions.Count + pending.Count > maximumExceptions)
                    {
                        return true;
                    }

                    foreach (var inner in aggregate.InnerExceptions)
                    {
                        pending.Push(inner);
                    }
                }
                else if (current.InnerException != null)
                {
                    pending.Push(current.InnerException);
                }
            }

            // Excessively deep/broad wrappers are not safe to render in full.
            return pending.Count > 0;
        }
    }
}
