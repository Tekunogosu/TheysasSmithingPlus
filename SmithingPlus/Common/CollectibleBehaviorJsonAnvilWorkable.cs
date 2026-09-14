#nullable enable
using System;
using JetBrains.Annotations;
using SmithingPlus.Util;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace SmithingPlus.Common;

public sealed class CollectibleBehaviorJsonAnvilWorkable(CollectibleObject collObj)
    : CollectibleBehaviorAnvilWorkable(collObj)
{
    private byte[,,]? _fixedVoxels;

    /// <summary>
    ///     The voxel pattern for this collectible.
    ///     <para>
    ///         A pattern with random voxels has to be generated per read, since each placement is meant to
    ///         differ. A pattern without them is the same array every time, so it is generated once: reads
    ///         are frequent (the handbook and every placement attempt) and each generation fills a
    ///         16x6x16 array.
    ///     </para>
    /// </summary>
    protected override byte[,,] Voxels => HasExtraVoxels
        ? GenVoxelsFromJsonPatternWithExtra(Pattern, Api?.World.Rand, ExtraVoxelChance)
        : _fixedVoxels ??= GenVoxelsFromJsonPattern(Pattern);

    private string[][] Pattern { get; set; } = [[]];
    private bool HasExtraVoxels { get; set; }
    private float ExtraVoxelChance { get; set; } = 0.5f;
    private EnumHelveWorkableMode HelveWorkableMode { get; set; } = EnumHelveWorkableMode.NotWorkable;

    public override void Initialize(JsonObject properties)
    {
        base.Initialize(properties);
        HasExtraVoxels = properties[PropertyKeys.HasExtraVoxels].Exists &&
                         properties[PropertyKeys.HasExtraVoxels].AsBool();
        ExtraVoxelChance = properties[PropertyKeys.ExtraVoxelChance].Exists
            ? properties[PropertyKeys.ExtraVoxelChance].AsFloat(0.5f)
            : 0f;
        HelveWorkableMode = properties[PropertyKeys.HelveWorkableMode].Exists
            ? Enum.Parse<EnumHelveWorkableMode>(
                properties[PropertyKeys.HelveWorkableMode].AsString(nameof(EnumHelveWorkableMode.NotWorkable)))
            : EnumHelveWorkableMode.NotWorkable;
        var jsonPattern = properties[PropertyKeys.Voxels].Exists ? properties[PropertyKeys.Voxels].AsArray() : null;
        if (jsonPattern is not { Length: > 0 }) return;

        // Layer of rows of strings, built directly. A row the JSON leaves out becomes an empty layer rather
        // than a null one, so the generator below can index it without checking.
        var pattern = new string[jsonPattern.Length][];
        for (var layer = 0; layer < jsonPattern.Length; layer++)
        {
            var rows = jsonPattern[layer]?.AsArray();
            if (rows == null)
            {
                pattern[layer] = [];
                continue;
            }

            var built = new string[rows.Length];
            for (var row = 0; row < rows.Length; row++) built[row] = rows[row]?.AsString() ?? "";
            pattern[layer] = built;
        }

        Pattern = pattern;
    }

    public override EnumHelveWorkableMode GetHelveWorkableMode(ItemStack stack, BlockEntityAnvil beAnvil)
    {
        return HelveWorkableMode;
    }

    // Only use always present voxels for handbook
    public override int VoxelCountForHandbook(ItemStack stack)
    {
        return GenVoxelsFromJsonPattern(Pattern).MaterialCount();
    }

    /// <summary>
    ///     Generates voxels from a JSON pattern.
    ///     The pattern is expected to be a 3D array of strings,
    ///     where each string represents a layer of the recipe.
    ///     Each character in the string can be:
    ///     '#' for a full voxel,
    ///     '*' for a slag voxel,
    ///     '_' or any character for an empty voxel.
    ///     The generated voxels will be centered in a 16x6x16 array.
    /// </summary>
    /// <param name="pattern">The JSON pattern to generate voxels from.</param>
    private static byte[,,] GenVoxelsFromJsonPattern(string[][] pattern)
    {
        return GenVoxelsFromJsonPatternWithExtra(pattern);
    }

    /// <summary>
    ///     Generates voxels from a JSON pattern with extra voxel chance.
    ///     The pattern is expected to be a 3D array of strings,
    ///     where each string represents a layer of the recipe.
    ///     Each character in the string can be:
    ///     '#' for a full voxel,
    ///     '*' for a slag voxel,
    ///     'o' for a random full voxel (with a chance defined by extraVoxelChance),
    ///     'x' for a random slag voxel (with a chance defined by extraVoxelChance),
    ///     '?' for a random voxel (either full or slag with 50% chance),
    ///     '_' or any character for an empty voxel.
    ///     The generated voxels will be centered in a 16x6x16 array.
    /// </summary>
    /// <param name="pattern">The JSON pattern to generate voxels from.</param>
    /// <param name="rand">An optional random number generator. If null, a default one will be used.</param>
    /// <param name="extraVoxelChance">The chance of generating extra voxels (for 'o' and 'x' characters).</param>
    public static byte[,,] GenVoxelsFromJsonPatternWithExtra(
        string[][] pattern,
        Random? rand = null,
        float extraVoxelChance = 0.0f)
    {
        var hasExtraVoxels = rand != null;
        // Fallback if api is not available
        extraVoxelChance = hasExtraVoxels ? extraVoxelChance : 0;
        var voxels = new byte[16, 6, 16];

        // An empty grid for an empty pattern. The default pattern is one empty layer, which a collectible
        // declaring no voxels keeps, and reading a row out of it would throw rather than yield nothing.
        if (pattern.Length == 0 || pattern[0].Length == 0 || pattern[0][0].Length == 0) return voxels;

        var length = pattern[0][0].Length;
        var width = pattern[0].Length;
        var height = pattern.Length;
        // Center the recipe to the horizontal middle
        var startX = (16 - width) / 2;
        var startZ = (16 - length) / 2;
        for (var x = 0; x < Math.Min(width, 16); x++)
        for (var y = 0; y < Math.Min(height, 6); y++)
        for (var z = 0; z < Math.Min(length, 16); z++)
        {
            var c = pattern[y][x][z];
            var b = c switch
            {
                '#' => EnumVoxelMaterial.Metal, // always full
                '*' => EnumVoxelMaterial.Slag, // always slag
                'o' => rand?.NextDouble() < extraVoxelChance
                    ? EnumVoxelMaterial.Metal
                    : EnumVoxelMaterial.Empty, // random full
                'x' => rand?.NextDouble() < extraVoxelChance
                    ? EnumVoxelMaterial.Slag
                    : EnumVoxelMaterial.Empty, // random slag
                '?' => hasExtraVoxels
                    ? rand?.NextDouble() < 0.5f ? EnumVoxelMaterial.Metal : EnumVoxelMaterial.Slag
                    : EnumVoxelMaterial.Empty, // random full/slag
                _ => EnumVoxelMaterial.Empty // empty (_ or space or anything else)
            };
            voxels[z + startZ, y, x + startX] = (byte)b;
        }

        return voxels;
    }

    private static class PropertyKeys
    {
        public const string HasExtraVoxels = "hasExtraVoxels";
        public const string ExtraVoxelChance = "extraVoxelChance";
        public const string HelveWorkableMode = "helveWorkableMode";
        public const string Voxels = "voxels";
    }
}