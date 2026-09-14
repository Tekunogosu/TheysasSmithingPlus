#nullable enable
using Vintagestory.API.Common;

namespace SmithingPlus.Util;

public static class ItemExtensions
{
    /// <summary>
    ///     The item of the same code with one variant part replaced, or null if there is no such item.
    ///     The item-typed case of
    ///     <see cref="CollectibleExtensions.CollectibleWithVariant" />, which resolves the world lookup for
    ///     either item class.
    /// </summary>
    public static Item? ItemWithVariant(this Item item, string type, string value)
    {
        return item.CollectibleWithVariant(type, value) as Item;
    }
}
