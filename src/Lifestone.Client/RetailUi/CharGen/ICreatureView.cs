using System.Numerics;
using Lifestone.Client.Avatars;
using Lifestone.Client.Effects;

namespace Lifestone.Client.RetailUi.CharacterCreation;

/// <summary>
/// A creature in a viewport element's hole, engine-agnostic: what the Godot layer's binder (<c>CharGenViewportBinder</c>) draws through the portal track's creature-mode view. Character creation's
/// <see cref="Cg3DViewModel"/> and the paperdoll's <c>PaperDollFigure</c> (M3 2.2 b4) are the two; both are a <see cref="CreaturePreview"/> with a camera and a version that changes whenever anything
/// drawn changed.
/// </summary>
public interface ICreatureView
{
    /// <summary>Changes whenever anything the host draws changed: the creature, its pose, the camera, the heading.</summary>
    int Version { get; }

    /// <summary>The posed creature, or null while there is none.</summary>
    CreaturePreview? Preview { get; }

    /// <summary>The camera's position, in AC space (<c>m_vectPosition</c>).</summary>
    Vector3 CameraPosition { get; }

    /// <summary>The camera's direction as retail's <c>CreatureMode::SetCameraDirection_Degrees</c> takes it.</summary>
    Vector3 CameraDirection { get; }

    /// <summary>The backdrop's parts, posed at the origin; empty when there is none.</summary>
    ReadOnlySpan<PartDraw> BackdropDraws { get; }

    /// <summary>The backdrop's Setup, or 0.</summary>
    uint BackdropSetupId { get; }
}
