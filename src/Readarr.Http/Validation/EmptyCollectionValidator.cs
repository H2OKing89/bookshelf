using System.Collections.Generic;
using FluentValidation.Internal;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Validation;

namespace Readarr.Http.Validation
{
    public class EmptyCollectionValidator<T> : ReusablePropertyValidator<IEnumerable<T>>
    {
        protected override string GetDefaultMessageTemplate() => "Collection Must Be Empty";

        protected override bool IsValid(IEnumerable<T> value, MessageFormatter formatter)
        {
            if (value == null)
            {
                return true;
            }

            return value.Empty();
        }
    }
}
