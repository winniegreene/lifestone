using System.Collections.Generic;
using Lifestone.Client.Play;
using Lifestone.Client.RetailUi.CharacterCreation;
using Lifestone.Client.Session.StandIn;
using Lifestone.Data.Appearance;
using Lifestone.Game.Core;
using Lifestone.Game.Diagnostics;

namespace Lifestone.Game.Sessions;

/// <summary>
/// M3 4.2 p6: the looks of the stand-in server's humans. A real server sends a human's clothing in its CreateObject; the stand-in used to send none, so the player, the placeholder vendors and the
/// corpse were drawn as the bare base body (the naked figures of the Holtburg frames). The player wears the stand-in look of explore mode (<see cref="OfflinePlayerLook"/> through CharGen's composition); each
/// placeholder role wears another garment of the same lists. Every choice is a placeholder, logged once per role (<c>TODO(m3-standin-human-look)</c>).
/// </summary>
public sealed partial class PlayChecks
{
    /// <summary>The garment (index into CharGen's lists) and shade each placeholder role wears; a name not listed wears the player's look.</summary>
    private static readonly (string Name, int Garment, double Shade)[] RoleGarments =
    [
        ("Stand-in Vendor", 2, 0.8),
        ("Stand-in Player Killer", 1, 0.9),
        ("Stand-in Player", 2, 0.3),
        ("Stand-in Apparition", 1, 0.5),
    ];

    /// <summary>
    /// Gives <see cref="Visit"/> and <see cref="ReplayVisit"/> their humans' looks, composed from the dats (before the session host is installed: the visit is read when the player enters). With no dats the visits keep the
    /// base body and say so.
    /// </summary>
    public static void SupplyHumanLooks(GameServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Dats is not { } dats)
        {
            Log.Warn(LogCategory, "TODO(m3-standin-human-look): no dats, so the stand-in's humans are sent as the bare base body");
            return;
        }

        try
        {
            var data = Cg3DViewData.FromDats(dats, services.Textures, services.GfxObjMeshes);
            var player = Look(data, OfflinePlayerLook.Create(data.Record, data.Dats), "the player");
            var roles = new Dictionary<string, ObjDesc>();
            foreach (var (name, garment, shade) in RoleGarments)
            {
                roles[name] = Look(data, OfflinePlayerLook.Create(data.Record, data.Dats, garment, shade), name);
            }

            foreach (var visit in new[] { Visit, ReplayVisit })
            {
                visit.SetHumanLooks(player, roles);
                visit.Dresser = new DatDresser(data.Dats, player);
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentOutOfRangeException)
        {
            Log.Error(LogCategory, "TODO(m3-standin-human-look): the stand-in's humans cannot be dressed; they are sent as the bare base body", ("error", ex.Message));
        }
    }

    /// <summary>
    /// The stand-in's dresser over the dats (M3 4.2 p6 follow-up): ACE's <c>Creature.CalculateObjDesc</c> reduced to what the stand-in's player can wear, which is each worn piece's ClothingTable applied for the
    /// human Setup on top of the player's base look through <c>ClothingTable::BuildObjDesc</c> (11.6096 0x005A89B0), the palette template and shade the piece was created with. A template the table lacks: ACE
    /// (which the stand-in imitates) takes the table's first template, <c>Creature_Networking.cs</c> 190-196; retail's <c>BuildObjDesc</c> fails there and draws the piece undyed (docs: the Gold answer).
    /// </summary>
    private sealed class DatDresser(IAppearanceDats dats, ObjDesc baseLook) : Lifestone.Client.Session.StandIn.IStandInDresser
    {
        private readonly HashSet<string> _told = [];

        public ObjDesc Player(IReadOnlyList<Lifestone.Client.Session.StandIn.StandInWorn> worn)
        {
            var into = baseLook.ToBuilder();
            foreach (var piece in worn)
            {
                Apply(into, piece, StandInVisit.HumanSetup);
            }

            return into.Build();
        }

        public ObjDesc Item(Lifestone.Client.Session.StandIn.StandInWorn piece)
        {
            // TODO(q7-item-palette-base): the weenie's PaletteBase is not read; the human base palette stands in (a held or placed piece only).
            var into = new ObjDescBuilder { PaletteId = baseLook.PaletteId };
            Apply(into, piece, piece.ItemSetup);
            return into.Build();
        }

        private void Apply(ObjDescBuilder into, Lifestone.Client.Session.StandIn.StandInWorn piece, uint setup)
        {
            var table = dats.GetClothingTable(piece.ClothingBase);
            if (table is null)
            {
                Tell($"missing-{piece.ClothingBase:X8}", $"ClothingTable 0x{piece.ClothingBase:X8} not found; the piece is not drawn");
                return;
            }

            var template = piece.Palette;
            if (template != 0 && !table.ClothingSubPalEffects.ContainsKey((uint)template) && table.ClothingSubPalEffects.Count > 0)
            {
                var first = table.ClothingSubPalEffects.Keys.First();
                Tell($"template-{piece.ClothingBase:X8}-{template}", $"ClothingTable 0x{piece.ClothingBase:X8} has no palette template {template}; ACE takes the first ({first})");
                template = (int)first;
            }

            var result = ClothingObjDesc.Build(table, setup, (uint)template, new ShadePackage(piece.Shade), dats, into);
            if (!result.AllApplied)
            {
                Tell($"problem-{piece.ClothingBase:X8}", string.Join("; ", result.Problems));
            }
        }

        private void Tell(string key, string what)
        {
            if (_told.Add(key))
            {
                Log.Warn(LogCategory, "stand-in dresser: " + what);
            }
        }
    }

    private static ObjDesc Look(Cg3DViewData data, CharGenLook look, string role)
    {
        var composition = CharGenAppearance.Compose(data.Record, look, data.Dats);
        if (!composition.Result.AllApplied)
        {
            Log.Warn(LogCategory, "a stand-in human's look has problems (retail ignores them)", ("role", role), ("problems", string.Join("; ", composition.Result.Problems)));
        }

        Log.Info(LogCategory, "TODO(m3-standin-human-look): placeholder garments (the stand-in has no weenie database)", ("role", role), ("parts", composition.ObjDesc.PartChanges.Count), ("textures", composition.ObjDesc.TextureChanges.Count), ("palettes", composition.ObjDesc.SubPalettes.Count));
        return composition.ObjDesc;
    }
}
