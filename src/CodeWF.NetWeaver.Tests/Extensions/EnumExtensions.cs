using System.ComponentModel;
using System.Reflection;

namespace CodeWF.NetWeaver.Tests.Extensions
{
    public static class EnumExtensions
    {
        public static string Description(this Enum value)
        {
            var enumType = value.GetType();

            var isFlagsEnum = enumType.GetCustomAttribute<FlagsAttribute>() != null;
            if (!isFlagsEnum) return GetDescription(value);

            var descriptions = new List<string>();
            foreach (Enum enumValue in Enum.GetValues(enumType))
            {
                if (Convert.ToInt64(enumValue) == 0) continue;

                if (value.HasFlag(enumValue)) descriptions.Add(GetDescription(enumValue));
            }

            return descriptions.Count <= 0 ? GetDescription(value) : string.Join(",", descriptions);
        }

        private static string GetDescription(Enum value)
        {
            var fieldInfo = value.GetType().GetField(value.ToString());
            var attribute =
                Attribute.GetCustomAttribute(fieldInfo!, typeof(DescriptionAttribute)) as DescriptionAttribute;
            return attribute?.Description ?? value.ToString();
        }
    }
}