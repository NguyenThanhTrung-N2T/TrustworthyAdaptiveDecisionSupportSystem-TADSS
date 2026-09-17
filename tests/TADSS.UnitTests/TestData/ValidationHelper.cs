using System.ComponentModel.DataAnnotations;

namespace TADSS.UnitTests.TestData;

internal static class ValidationHelper
{
    public static IList<ValidationResult> Validate(object model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var results = new List<ValidationResult>();
        Validator.TryValidateObject(
            model,
            new ValidationContext(model, serviceProvider: null, items: null),
            results,
            validateAllProperties: true);

        return results;
    }
}
