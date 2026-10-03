using System.Reflection;

namespace TADSS.UnitTests.TestData;

/// <summary>
/// Quét bằng reflection các kiểu mà một kiểu khai báo tham chiếu tới (trường, property, tham số và kiểu trả về của method/constructor,
/// interface, kiểu cơ sở; có bung kiểu mảng và đối số generic), dùng chung cho test kiến trúc kiểm ranh giới tầng
/// (<c>NoLegacyV1Tests</c>, <c>CompositionRootBoundaryTests</c>).
/// </summary>
internal static class TypeReferenceScanner
{
    private const BindingFlags AllDeclared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    /// <summary>Mọi kiểu xuất hiện trong chữ ký/trường/quan hệ kế thừa của <paramref name="type"/> (chỉ phần khai báo trực tiếp).</summary>
    public static IEnumerable<Type> ReferencedTypes(Type type)
    {
        var methods = type.GetMethods(AllDeclared);
        var direct = type.GetProperties(AllDeclared).Select(property => property.PropertyType)
            .Concat(type.GetFields(AllDeclared).Select(field => field.FieldType))
            .Concat(methods.Select(method => method.ReturnType))
            .Concat(methods.SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType)))
            .Concat(type.GetConstructors(AllDeclared).SelectMany(ctor => ctor.GetParameters().Select(parameter => parameter.ParameterType)))
            .Concat(type.GetInterfaces());

        if (type.BaseType is { } baseType)
        {
            direct = direct.Append(baseType);
        }

        return direct.SelectMany(Flatten);
    }

    /// <summary>Kiểu gốc của một kiểu tham chiếu: bỏ tham số generic đã đóng để so khớp theo namespace/assembly của định nghĩa.</summary>
    public static Type Definition(Type type) => type.IsGenericType ? type.GetGenericTypeDefinition() : type;

    /// <summary><paramref name="ns"/> bằng <paramref name="root"/> hoặc nằm dưới <paramref name="root"/>.</summary>
    public static bool IsInNamespace(string? ns, string root)
    {
        return ns is not null && (ns == root || ns.StartsWith(root + ".", StringComparison.Ordinal));
    }

    private static IEnumerable<Type> Flatten(Type type)
    {
        yield return type;

        var element = type.HasElementType ? type.GetElementType() : null;
        if (element is not null)
        {
            foreach (var inner in Flatten(element))
            {
                yield return inner;
            }
        }

        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments().SelectMany(Flatten))
            {
                yield return argument;
            }
        }
    }
}
