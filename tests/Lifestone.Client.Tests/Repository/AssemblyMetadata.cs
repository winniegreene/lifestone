using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace Lifestone.Client.Tests.Repository;

/// <summary>
/// What a built Lifestone assembly references, read from its metadata and IL (System.Reflection.Metadata, part of the
/// shared framework): the compiled counterpart of the source scans, which a <c>using static</c>, an alias or a
/// compile-linked file cannot dodge.
/// </summary>
internal sealed class AssemblyMetadata
{
    private static readonly Lazy<Dictionary<int, OpCode>> OpCodesByValue = new(() =>
        typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => (OpCode)f.GetValue(null)!)
            .ToDictionary(o => o.Value & 0xFFFF));

    private AssemblyMetadata(string name, string[] references, string[] typeReferences, MemberUse[] memberUses)
    {
        Name = name;
        AssemblyReferences = references;
        TypeReferences = typeReferences;
        MemberUses = memberUses;
    }

    /// <summary>The assembly's simple name.</summary>
    public string Name { get; }

    /// <summary>The simple names of the assemblies it references.</summary>
    public IReadOnlyList<string> AssemblyReferences { get; }

    /// <summary>The full names (<c>Namespace.Outer+Inner</c>) of the external types it references.</summary>
    public IReadOnlyList<string> TypeReferences { get; }

    /// <summary>Every external member that its IL calls, loads or takes a token of, with the calling type.</summary>
    public IReadOnlyList<MemberUse> MemberUses { get; }

    /// <summary>Reads a built assembly.</summary>
    public static AssemblyMetadata Read(string path)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var md = pe.GetMetadataReader();
        var references = md.AssemblyReferences.Select(h => md.GetString(md.GetAssemblyReference(h).Name)).ToArray();
        var typeReferences = md.TypeReferences.Select(h => TypeName(md, h)).ToArray();
        var uses = new List<MemberUse>();
        foreach (var typeHandle in md.TypeDefinitions)
        {
            var type = md.GetTypeDefinition(typeHandle);
            var caller = OutermostTypeName(md, type);
            foreach (var methodHandle in type.GetMethods())
            {
                var method = md.GetMethodDefinition(methodHandle);
                if (method.RelativeVirtualAddress != 0)
                {
                    ReadBody(md, pe.GetMethodBody(method.RelativeVirtualAddress), caller, uses);
                }
            }
        }

        return new AssemblyMetadata(md.GetString(md.GetAssemblyDefinition().Name), references, typeReferences, [.. uses]);
    }

    private static void ReadBody(MetadataReader md, MethodBodyBlock body, string caller, List<MemberUse> uses)
    {
        var il = body.GetILReader();
        while (il.RemainingBytes > 0)
        {
            int value = il.ReadByte();
            if (value == 0xFE)
            {
                value = 0xFE00 | il.ReadByte();
            }

            if (!OpCodesByValue.Value.TryGetValue(value, out var op))
            {
                throw new InvalidOperationException($"unknown IL opcode 0x{value:X} in a method of {caller}");
            }

            switch (op.OperandType)
            {
                case OperandType.InlineMethod or OperandType.InlineField or OperandType.InlineTok or OperandType.InlineType:
                    Record(md, MetadataTokens.EntityHandle(il.ReadInt32()), caller, uses);
                    break;
                case OperandType.InlineSwitch:
                    var targets = il.ReadInt32();
                    il.Offset += 4 * targets;
                    break;
                case OperandType.InlineNone:
                    break;
                case OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar:
                    il.Offset += 1;
                    break;
                case OperandType.InlineVar:
                    il.Offset += 2;
                    break;
                case OperandType.InlineI8 or OperandType.InlineR:
                    il.Offset += 8;
                    break;
                default:
                    il.Offset += 4; // InlineBrTarget, InlineI, InlineSig, InlineString, ShortInlineR
                    break;
            }
        }
    }

    private static void Record(MetadataReader md, EntityHandle handle, string caller, List<MemberUse> uses)
    {
        switch (handle.Kind)
        {
            case HandleKind.MemberReference:
                var member = md.GetMemberReference((MemberReferenceHandle)handle);
                uses.Add(new MemberUse(caller, TypeName(md, member.Parent), md.GetString(member.Name)));
                break;
            case HandleKind.MethodSpecification:
                Record(md, md.GetMethodSpecification((MethodSpecificationHandle)handle).Method, caller, uses);
                break;
            case HandleKind.TypeReference or HandleKind.TypeSpecification:
                uses.Add(new MemberUse(caller, TypeName(md, handle), ""));
                break;
        }
    }

    /// <summary>
    /// The full name of a referenced type: <c>Namespace.Name</c>, <c>Outer+Inner</c> for nested types, and the generic
    /// definition (<c>DatReaderWriter.Types.QualifiedDataId`1</c>) for an instantiation. Empty for other type specs
    /// (arrays, pointers) and for module-level members.
    /// </summary>
    private static string TypeName(MetadataReader md, EntityHandle handle)
    {
        switch (handle.Kind)
        {
            case HandleKind.TypeReference:
                var reference = md.GetTypeReference((TypeReferenceHandle)handle);
                var name = md.GetString(reference.Name);
                return reference.ResolutionScope.Kind == HandleKind.TypeReference
                    ? TypeName(md, reference.ResolutionScope) + "+" + name
                    : Qualify(md.GetString(reference.Namespace), name);
            case HandleKind.TypeDefinition:
                var definition = md.GetTypeDefinition((TypeDefinitionHandle)handle);
                var declaring = definition.GetDeclaringType();
                return declaring.IsNil
                    ? Qualify(md.GetString(definition.Namespace), md.GetString(definition.Name))
                    : TypeName(md, declaring) + "+" + md.GetString(definition.Name);
            case HandleKind.TypeSpecification:
                var blob = md.GetBlobReader(md.GetTypeSpecification((TypeSpecificationHandle)handle).Signature);
                if (blob.ReadSignatureTypeCode() != SignatureTypeCode.GenericTypeInstance)
                {
                    return "";
                }

                blob.ReadSignatureTypeCode(); // CLASS or VALUETYPE
                return TypeName(md, blob.ReadTypeHandle());
            default:
                return "";
        }
    }

    private static string OutermostTypeName(MetadataReader md, TypeDefinition type)
    {
        while (!type.GetDeclaringType().IsNil)
        {
            type = md.GetTypeDefinition(type.GetDeclaringType());
        }

        return Qualify(md.GetString(type.Namespace), md.GetString(type.Name));
    }

    private static string Qualify(string ns, string name) => ns.Length == 0 ? name : ns + "." + name;
}

/// <summary>
/// One use of an external member (or, with an empty <see cref="Member"/>, of an external type) in IL.
/// </summary>
/// <param name="Caller">The outermost type whose method holds the IL (closures and state machines count as their outer type).</param>
/// <param name="Type">The member's declaring type (<c>Namespace.Name</c>; generic definitions keep their arity suffix).</param>
/// <param name="Member">The member's metadata name (<c>get_UtcNow</c>, <c>.ctor</c>, <c>Get</c>).</param>
internal readonly record struct MemberUse(string Caller, string Type, string Member);
