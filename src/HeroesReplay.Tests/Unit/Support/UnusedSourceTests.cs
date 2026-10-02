using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace HeroesReplay.Tests.Unit.Support;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class UnusedSourceTests
{
    private const string RewardHandler = "HeroesReplay.Core.Twitch.RedeemedRewards.IRewardHandler";

    private const string MessageHandler = "HeroesReplay.Core.Twitch.ChatMessages.IMessageHandler";

    [Fact]
    public void CoreAndCli_HaveNoUnreferencedTypes()
    {
        string[] unused = UnusedSourceScan.Find(AppContext.BaseDirectory);
        Assert.True(
            unused.Length == 0,
            "Unreferenced Core or CLI types. Delete them, or reference them from startup, a test, or the handler scan:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, unused)
        );
    }

    private static class UnusedSourceScan
    {
        private static readonly Dictionary<int, OperandType> OperandTypes = BuildOperandTypes();

        public static string[] Find(string outputDirectory)
        {
            if (!OperandTypes.ContainsKey(0x28))
            {
                throw new InvalidOperationException("IL opcode map is missing call.");
            }

            if (!OperandTypes.ContainsKey((ushort)OpCodes.Constrained.Value))
            {
                throw new InvalidOperationException("IL opcode map is missing constrained.");
            }

            using var core = LoadedModule.Open(
                Path.Combine(outputDirectory, "HeroesReplay.Core.dll")
            );
            using var cli = LoadedModule.Open(Path.Combine(outputDirectory, "heroesreplay.dll"));
            using var tests = LoadedModule.Open(
                Path.Combine(outputDirectory, "heroesreplay.tests.dll")
            );
            using var client = LoadedModule.Open(
                Path.Combine(outputDirectory, "HeroesReplay.HeroesProfile.Client.dll")
            );

            var candidates = new Dictionary<TypeKey, Candidate>();
            Index(core, candidates);
            Index(cli, candidates);

            var edges = new Dictionary<TypeKey, HashSet<TypeKey>>();
            var bases = new Dictionary<TypeKey, TypeKey>();
            var interfaces = new Dictionary<TypeKey, List<TypeKey>>();
            var roots = new HashSet<TypeKey>();

            Walk(core, candidates, edges, bases, interfaces, roots, recordEdges: true);
            Walk(cli, candidates, edges, bases, interfaces, roots, recordEdges: true);
            Walk(tests, candidates, edges, bases, interfaces, roots, recordEdges: false);
            Walk(client, candidates, edges, bases, interfaces, roots, recordEdges: false);
            AddEntryPoint(cli, roots);
            AddReflectionRoots(candidates, bases, interfaces, roots);
            RootSourceMentions(candidates, roots, outputDirectory);

            var reachable = new HashSet<TypeKey>(roots);
            var pending = new Queue<TypeKey>(roots);
            while (pending.Count > 0)
            {
                TypeKey type = pending.Dequeue();
                if (!edges.TryGetValue(type, out HashSet<TypeKey> next))
                {
                    continue;
                }

                foreach (TypeKey target in next)
                {
                    if (reachable.Add(target))
                    {
                        pending.Enqueue(target);
                    }
                }
            }

            return candidates
                .Values.Where(candidate => !reachable.Contains(candidate.Key))
                .Select(candidate => candidate.Key.Assembly + ":" + candidate.Key.FullName)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
        }

        private static void Index(LoadedModule module, Dictionary<TypeKey, Candidate> candidates)
        {
            MetadataReader reader = module.Reader;
            foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
            {
                TypeDefinition definition = reader.GetTypeDefinition(handle);
                if (IsDirectUserType(reader, definition) == false)
                {
                    continue;
                }

                var key = new TypeKey(module.AssemblyName, FullName(reader, handle));
                bool isInterface = (definition.Attributes & TypeAttributes.Interface) != 0;
                bool isValueType = IsValueOrEnum(reader, definition.BaseType);
                candidates[key] = new Candidate(
                    key,
                    isInterface == false && isValueType == false,
                    Encoding.UTF8.GetBytes(key.FullName)
                );
            }
        }

        private static void Walk(
            LoadedModule module,
            Dictionary<TypeKey, Candidate> candidates,
            Dictionary<TypeKey, HashSet<TypeKey>> edges,
            Dictionary<TypeKey, TypeKey> bases,
            Dictionary<TypeKey, List<TypeKey>> interfaces,
            HashSet<TypeKey> roots,
            bool recordEdges
        )
        {
            var visitor = new Visitor(
                module,
                candidates,
                edges,
                bases,
                interfaces,
                roots,
                recordEdges
            );
            MetadataReader reader = module.Reader;
            foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
            {
                visitor.ScanType(handle);
            }

            visitor.ScanAttributes(
                reader.GetAssemblyDefinition().GetCustomAttributes(),
                source: null
            );
            visitor.ScanAttributes(
                reader.GetModuleDefinition().GetCustomAttributes(),
                source: null
            );
        }

        private static void RootSourceMentions(
            Dictionary<TypeKey, Candidate> candidates,
            HashSet<TypeKey> roots,
            string outputDirectory
        )
        {
            // const fields and [JsonConverter(typeof(...))] arguments are not TypeRefs.
            // A non-declaration mention in hand-written source keeps the type.
            string src = Path.Combine(RepoRoot(outputDirectory), "src");
            var texts = new List<string>();
            foreach (
                string path in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            )
            {
                if (IsGeneratedPath(path))
                {
                    continue;
                }

                texts.Add(File.ReadAllText(path));
            }

            foreach (Candidate candidate in candidates.Values)
            {
                if (roots.Contains(candidate.Key))
                {
                    continue;
                }

                string simple = SimpleName(candidate.Key.FullName);
                if (simple.Length == 0)
                {
                    continue;
                }

                foreach (string text in texts)
                {
                    if (Mentioned(text, simple))
                    {
                        roots.Add(candidate.Key);
                        break;
                    }
                }
            }
        }

        private static string RepoRoot(string start)
        {
            var dir = new DirectoryInfo(start);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "heroes-replay.slnx")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            throw new InvalidOperationException("heroes-replay.slnx was not found.");
        }

        private static bool IsGeneratedPath(string path)
        {
            return path.Contains(
                    $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase
                )
                || path.Contains(
                    $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase
                )
                || path.Contains(
                    $"{Path.DirectorySeparatorChar}Generated{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase
                );
        }

        private static string SimpleName(string fullName)
        {
            int split = Math.Max(fullName.LastIndexOf('.'), fullName.LastIndexOf('+'));
            string simple = split < 0 ? fullName : fullName[(split + 1)..];
            int arity = simple.IndexOf('`');
            return arity < 0 ? simple : simple[..arity];
        }

        private static bool Mentioned(string text, string simpleName)
        {
            int start = 0;
            while (start < text.Length)
            {
                int at = text.IndexOf(simpleName, start, StringComparison.Ordinal);
                if (at < 0)
                {
                    return false;
                }

                int end = at + simpleName.Length;
                bool before = at == 0 || IsIdentifierChar(text[at - 1]) == false;
                bool after = end == text.Length || IsIdentifierChar(text[end]) == false;
                if (before && after && IsTypeDeclaration(text, at) == false)
                {
                    return true;
                }

                start = end;
            }

            return false;
        }

        private static bool IsIdentifierChar(char value)
        {
            return char.IsLetterOrDigit(value) || value == '_';
        }

        private static bool IsTypeDeclaration(string text, int nameAt)
        {
            int line = text.LastIndexOf('\n', Math.Max(0, nameAt - 1));
            if (line < 0)
            {
                line = -1;
            }

            string prefix = text.Substring(line + 1, nameAt - line - 1);
            return Regex.IsMatch(
                prefix,
                @"\b(?:class|struct|interface|enum|record|delegate)\s+(?:partial\s+)?(?:class|struct)?\s*$"
            );
        }

        private static void AddEntryPoint(LoadedModule module, HashSet<TypeKey> roots)
        {
            CorHeader header = module.Pe.PEHeaders.CorHeader;
            if (header == null || (header.Flags & CorFlags.NativeEntryPoint) != 0)
            {
                return;
            }

            EntityHandle token = MetadataTokens.EntityHandle(
                header.EntryPointTokenOrRelativeVirtualAddress
            );
            if (token.Kind != HandleKind.MethodDefinition)
            {
                return;
            }

            MethodDefinition method = module.Reader.GetMethodDefinition(
                (MethodDefinitionHandle)token
            );
            if (
                TryOwner(
                    module.Reader,
                    module.AssemblyName,
                    method.GetDeclaringType(),
                    out TypeKey key
                )
            )
            {
                roots.Add(key);
            }
        }

        private static void AddReflectionRoots(
            Dictionary<TypeKey, Candidate> candidates,
            Dictionary<TypeKey, TypeKey> bases,
            Dictionary<TypeKey, List<TypeKey>> interfaces,
            HashSet<TypeKey> roots
        )
        {
            foreach (Candidate candidate in candidates.Values)
            {
                if (candidate.IsClass && ImplementsScannedHandler(candidate.Key, bases, interfaces))
                {
                    roots.Add(candidate.Key);
                }
            }
        }

        private static bool ImplementsScannedHandler(
            TypeKey start,
            Dictionary<TypeKey, TypeKey> bases,
            Dictionary<TypeKey, List<TypeKey>> interfaces
        )
        {
            var seen = new HashSet<TypeKey>();
            var pending = new Stack<TypeKey>();
            pending.Push(start);
            while (pending.Count > 0)
            {
                TypeKey type = pending.Pop();
                if (seen.Add(type) == false)
                {
                    continue;
                }

                if (type.FullName == RewardHandler || type.FullName == MessageHandler)
                {
                    return true;
                }

                if (bases.TryGetValue(type, out TypeKey baseType))
                {
                    pending.Push(baseType);
                }

                if (interfaces.TryGetValue(type, out List<TypeKey> implemented))
                {
                    foreach (TypeKey item in implemented)
                    {
                        pending.Push(item);
                    }
                }
            }

            return false;
        }

        private static bool IsDirectUserType(MetadataReader reader, TypeDefinition definition)
        {
            string name = reader.GetString(definition.Name);
            return name.Contains('<') == false
                && name.Contains('>') == false
                && HasCompilerGenerated(reader, definition) == false;
        }

        private static bool TryOwner(
            MetadataReader reader,
            string assembly,
            TypeDefinitionHandle handle,
            out TypeKey key
        )
        {
            key = default;
            if (handle.IsNil)
            {
                return false;
            }

            TypeDefinition definition = reader.GetTypeDefinition(handle);
            if (IsDirectUserType(reader, definition) == false)
            {
                return TryOwner(reader, assembly, definition.GetDeclaringType(), out key);
            }

            key = new TypeKey(assembly, FullName(reader, handle));
            return true;
        }

        private static string FullName(MetadataReader reader, TypeDefinitionHandle handle)
        {
            TypeDefinition definition = reader.GetTypeDefinition(handle);
            string name = reader.GetString(definition.Name);
            TypeDefinitionHandle declaring = definition.GetDeclaringType();
            if (declaring.IsNil == false)
            {
                return FullName(reader, declaring) + "+" + name;
            }

            string ns = reader.GetString(definition.Namespace);
            return ns.Length == 0 ? name : ns + "." + name;
        }

        private static bool HasCompilerGenerated(MetadataReader reader, TypeDefinition definition)
        {
            foreach (CustomAttributeHandle handle in definition.GetCustomAttributes())
            {
                if (
                    AttributeTypeName(reader, reader.GetCustomAttribute(handle))
                    == "CompilerGeneratedAttribute"
                )
                {
                    return true;
                }
            }

            return false;
        }

        private static string AttributeTypeName(MetadataReader reader, CustomAttribute attribute)
        {
            EntityHandle parent = attribute.Constructor;
            if (parent.Kind == HandleKind.MemberReference)
            {
                parent = reader.GetMemberReference((MemberReferenceHandle)parent).Parent;
            }
            else if (parent.Kind == HandleKind.MethodDefinition)
            {
                parent = reader
                    .GetMethodDefinition((MethodDefinitionHandle)parent)
                    .GetDeclaringType();
                return reader.GetString(
                    reader.GetTypeDefinition((TypeDefinitionHandle)parent).Name
                );
            }

            if (parent.Kind != HandleKind.TypeReference)
            {
                return "";
            }

            return reader.GetString(reader.GetTypeReference((TypeReferenceHandle)parent).Name);
        }

        private static bool IsValueOrEnum(MetadataReader reader, EntityHandle handle)
        {
            if (handle.Kind != HandleKind.TypeReference)
            {
                return false;
            }

            TypeReference reference = reader.GetTypeReference((TypeReferenceHandle)handle);
            string name = reader.GetString(reference.Name);
            string ns = reader.GetString(reference.Namespace);
            return ns == "System" && (name == "ValueType" || name == "Enum");
        }

        private static Dictionary<int, OperandType> BuildOperandTypes()
        {
            var map = new Dictionary<int, OperandType>();
            foreach (
                FieldInfo field in typeof(OpCodes).GetFields(
                    BindingFlags.Public | BindingFlags.Static
                )
            )
            {
                if (field.FieldType != typeof(OpCode))
                {
                    continue;
                }

                var op = (OpCode)field.GetValue(null)!;
                map[(ushort)op.Value] = op.OperandType;
            }

            return map;
        }

        private readonly record struct TypeKey(string Assembly, string FullName);

        private sealed record Candidate(TypeKey Key, bool IsClass, byte[] Utf8);

        private sealed class Visitor : ISignatureTypeProvider<object, object>
        {
            private readonly LoadedModule module;
            private readonly Dictionary<TypeKey, Candidate> candidates;
            private readonly Dictionary<TypeKey, HashSet<TypeKey>> edges;
            private readonly Dictionary<TypeKey, TypeKey> bases;
            private readonly Dictionary<TypeKey, List<TypeKey>> interfaces;
            private readonly HashSet<TypeKey> roots;
            private readonly bool recordEdges;
            private TypeKey? source;

            public Visitor(
                LoadedModule module,
                Dictionary<TypeKey, Candidate> candidates,
                Dictionary<TypeKey, HashSet<TypeKey>> edges,
                Dictionary<TypeKey, TypeKey> bases,
                Dictionary<TypeKey, List<TypeKey>> interfaces,
                HashSet<TypeKey> roots,
                bool recordEdges
            )
            {
                this.module = module;
                this.candidates = candidates;
                this.edges = edges;
                this.bases = bases;
                this.interfaces = interfaces;
                this.roots = roots;
                this.recordEdges = recordEdges;
            }

            public void ScanType(TypeDefinitionHandle handle)
            {
                TypeDefinition definition = module.Reader.GetTypeDefinition(handle);
                bool directUser = IsDirectUserType(module.Reader, definition);
                source = null;
                if (
                    TryOwner(module.Reader, module.AssemblyName, handle, out TypeKey user)
                    && candidates.ContainsKey(user)
                )
                {
                    source = user;
                }

                if (directUser && source != null && definition.BaseType.IsNil == false)
                {
                    TypeKey? baseKey = Resolve(definition.BaseType);
                    if (baseKey != null)
                    {
                        bases[source.Value] = baseKey.Value;
                    }

                    Add(baseKey);
                }
                else if (definition.BaseType.IsNil == false)
                {
                    Add(Resolve(definition.BaseType));
                }

                foreach (
                    InterfaceImplementationHandle implemented in definition.GetInterfaceImplementations()
                )
                {
                    InterfaceImplementation face = module.Reader.GetInterfaceImplementation(
                        implemented
                    );
                    TypeKey? interfaceKey = Resolve(face.Interface);
                    if (directUser && source != null && interfaceKey != null)
                    {
                        if (interfaces.TryGetValue(source.Value, out List<TypeKey> list) == false)
                        {
                            list = new List<TypeKey>();
                            interfaces[source.Value] = list;
                        }

                        list.Add(interfaceKey.Value);
                    }

                    Add(interfaceKey);
                    ScanAttributes(face.GetCustomAttributes(), source);
                }

                foreach (GenericParameterHandle parameter in definition.GetGenericParameters())
                {
                    ScanConstraints(module.Reader.GetGenericParameter(parameter));
                }

                ScanAttributes(definition.GetCustomAttributes(), source);

                foreach (FieldDefinitionHandle fieldHandle in definition.GetFields())
                {
                    FieldDefinition field = module.Reader.GetFieldDefinition(fieldHandle);
                    field.DecodeSignature(this, null);
                    ScanAttributes(field.GetCustomAttributes(), source);
                }

                foreach (MethodDefinitionHandle methodHandle in definition.GetMethods())
                {
                    ScanMethod(methodHandle);
                }

                foreach (PropertyDefinitionHandle propertyHandle in definition.GetProperties())
                {
                    PropertyDefinition property = module.Reader.GetPropertyDefinition(
                        propertyHandle
                    );
                    property.DecodeSignature(this, null);
                    ScanAttributes(property.GetCustomAttributes(), source);
                }

                foreach (EventDefinitionHandle eventHandle in definition.GetEvents())
                {
                    EventDefinition ev = module.Reader.GetEventDefinition(eventHandle);
                    Add(Resolve(ev.Type));
                    ScanAttributes(ev.GetCustomAttributes(), source);
                }
            }

            public void ScanAttributes(CustomAttributeHandleCollection attributes, TypeKey? source)
            {
                TypeKey? previous = this.source;
                this.source = source;
                foreach (CustomAttributeHandle handle in attributes)
                {
                    CustomAttribute attribute = module.Reader.GetCustomAttribute(handle);
                    Add(Resolve(attribute.Constructor));
                    ScanBlob(attribute.Value);
                }

                this.source = previous;
            }

            private void ScanMethod(MethodDefinitionHandle handle)
            {
                MetadataReader reader = module.Reader;
                MethodDefinition method = reader.GetMethodDefinition(handle);
                method.DecodeSignature(this, null);
                foreach (GenericParameterHandle parameter in method.GetGenericParameters())
                {
                    ScanConstraints(reader.GetGenericParameter(parameter));
                }

                foreach (ParameterHandle parameter in method.GetParameters())
                {
                    ScanAttributes(reader.GetParameter(parameter).GetCustomAttributes(), source);
                }

                ScanAttributes(method.GetCustomAttributes(), source);
                if (method.RelativeVirtualAddress == 0)
                {
                    return;
                }

                MethodBodyBlock body = module.Pe.GetMethodBody(method.RelativeVirtualAddress);
                if (body.LocalSignature.IsNil == false)
                {
                    reader
                        .GetStandaloneSignature(body.LocalSignature)
                        .DecodeLocalSignature(this, null);
                }

                foreach (ExceptionRegion region in body.ExceptionRegions)
                {
                    if (region.CatchType.IsNil == false)
                    {
                        Add(Resolve(region.CatchType));
                    }
                }

                BlobReader il = body.GetILReader();
                while (il.RemainingBytes > 0)
                {
                    ReadInstruction(ref il);
                }
            }

            private void ScanConstraints(GenericParameter parameter)
            {
                foreach (GenericParameterConstraintHandle handle in parameter.GetConstraints())
                {
                    Add(Resolve(module.Reader.GetGenericParameterConstraint(handle).Type));
                }
            }

            private void ReadInstruction(ref BlobReader il)
            {
                int opcode = il.ReadByte();
                if (opcode == 0xFE)
                {
                    opcode = (0xFE << 8) | il.ReadByte();
                }

                if (OperandTypes.TryGetValue(opcode, out OperandType operand) == false)
                {
                    throw new InvalidOperationException($"Unknown IL opcode 0x{opcode:X}.");
                }

                switch (operand)
                {
                    case OperandType.InlineNone:
                        return;
                    case OperandType.ShortInlineBrTarget:
                    case OperandType.ShortInlineI:
                    case OperandType.ShortInlineVar:
                        il.ReadByte();
                        return;
                    case OperandType.InlineVar:
                        il.ReadUInt16();
                        return;
                    case OperandType.InlineBrTarget:
                    case OperandType.InlineI:
                    case OperandType.ShortInlineR:
                        il.ReadInt32();
                        return;
                    case OperandType.InlineI8:
                    case OperandType.InlineR:
                        il.ReadInt64();
                        return;
                    case OperandType.InlineString:
                        il.ReadInt32();
                        return;
                    case OperandType.InlineMethod:
                    case OperandType.InlineField:
                    case OperandType.InlineType:
                    case OperandType.InlineTok:
                    case OperandType.InlineSig:
                        Add(Resolve(MetadataTokens.EntityHandle(il.ReadInt32())));
                        return;
                    case OperandType.InlineSwitch:
                        int count = il.ReadInt32();
                        for (int i = 0; i < count; i++)
                        {
                            il.ReadInt32();
                        }

                        return;
                    default:
                        throw new InvalidOperationException($"Unhandled operand {operand}.");
                }
            }

            private void ScanBlob(BlobHandle handle)
            {
                if (handle.IsNil)
                {
                    return;
                }

                byte[] bytes = module.Reader.GetBlobBytes(handle);
                ReadOnlySpan<byte> span = bytes;
                if (span.IndexOf("HeroesReplay"u8) < 0)
                {
                    return;
                }

                foreach (Candidate candidate in candidates.Values)
                {
                    if (ContainsName(span, candidate.Utf8))
                    {
                        Add(candidate.Key);
                    }
                }
            }

            private static bool ContainsName(ReadOnlySpan<byte> haystack, byte[] name)
            {
                int start = 0;
                while (start < haystack.Length)
                {
                    int found = haystack.Slice(start).IndexOf(name);
                    if (found < 0)
                    {
                        return false;
                    }

                    int at = start + found;
                    int end = at + name.Length;
                    bool before = at == 0 || IsNameChar(haystack[at - 1]) == false;
                    bool after = end == haystack.Length || IsNameChar(haystack[end]) == false;
                    if (before && after)
                    {
                        return true;
                    }

                    start = at + 1;
                }

                return false;
            }

            private static bool IsNameChar(byte value)
            {
                return (value >= (byte)'A' && value <= (byte)'Z')
                    || (value >= (byte)'a' && value <= (byte)'z')
                    || (value >= (byte)'0' && value <= (byte)'9')
                    || value == (byte)'_'
                    || value == (byte)'`';
            }

            private void Add(TypeKey? target)
            {
                if (target == null || candidates.ContainsKey(target.Value) == false)
                {
                    return;
                }

                if (source == null || recordEdges == false)
                {
                    roots.Add(target.Value);
                    return;
                }

                if (source.Value.Equals(target.Value))
                {
                    return;
                }

                if (edges.TryGetValue(source.Value, out HashSet<TypeKey> set) == false)
                {
                    set = new HashSet<TypeKey>();
                    edges[source.Value] = set;
                }

                set.Add(target.Value);
            }

            private TypeKey? Resolve(EntityHandle handle)
            {
                MetadataReader reader = module.Reader;
                switch (handle.Kind)
                {
                    case HandleKind.TypeDefinition:
                        return TryOwner(
                            reader,
                            module.AssemblyName,
                            (TypeDefinitionHandle)handle,
                            out TypeKey defined
                        )
                            ? defined
                            : null;
                    case HandleKind.TypeReference:
                        return ResolveReference((TypeReferenceHandle)handle);
                    case HandleKind.TypeSpecification:
                        reader
                            .GetTypeSpecification((TypeSpecificationHandle)handle)
                            .DecodeSignature(this, null);
                        return null;
                    case HandleKind.MemberReference:
                        return Resolve(
                            reader.GetMemberReference((MemberReferenceHandle)handle).Parent
                        );
                    case HandleKind.MethodDefinition:
                        return Resolve(
                            reader
                                .GetMethodDefinition((MethodDefinitionHandle)handle)
                                .GetDeclaringType()
                        );
                    case HandleKind.FieldDefinition:
                        return Resolve(
                            reader
                                .GetFieldDefinition((FieldDefinitionHandle)handle)
                                .GetDeclaringType()
                        );
                    case HandleKind.MethodSpecification:
                        MethodSpecification specification = reader.GetMethodSpecification(
                            (MethodSpecificationHandle)handle
                        );
                        Add(Resolve(specification.Method));
                        specification.DecodeSignature(this, null);
                        return null;
                    case HandleKind.StandaloneSignature:
                        reader
                            .GetStandaloneSignature((StandaloneSignatureHandle)handle)
                            .DecodeLocalSignature(this, null);
                        return null;
                    default:
                        return null;
                }
            }

            private TypeKey? ResolveReference(TypeReferenceHandle handle)
            {
                MetadataReader reader = module.Reader;
                TypeReference reference = reader.GetTypeReference(handle);
                string name = reader.GetString(reference.Name);
                EntityHandle scope = reference.ResolutionScope;
                if (scope.Kind == HandleKind.TypeReference)
                {
                    TypeKey? parent = ResolveReference((TypeReferenceHandle)scope);
                    if (parent == null)
                    {
                        return null;
                    }

                    var nested = new TypeKey(
                        parent.Value.Assembly,
                        parent.Value.FullName + "+" + name
                    );
                    return candidates.ContainsKey(nested) ? nested : null;
                }

                string assembly;
                if (scope.Kind == HandleKind.AssemblyReference)
                {
                    assembly = reader.GetString(
                        reader.GetAssemblyReference((AssemblyReferenceHandle)scope).Name
                    );
                }
                else if (scope.Kind == HandleKind.ModuleDefinition)
                {
                    assembly = module.AssemblyName;
                }
                else
                {
                    return null;
                }

                string ns = reader.GetString(reference.Namespace);
                string full = ns.Length == 0 ? name : ns + "." + name;
                var key = new TypeKey(assembly, full);
                return candidates.ContainsKey(key) ? key : null;
            }

            public object GetArrayType(object elementType, ArrayShape shape) => null;

            public object GetByReferenceType(object elementType) => null;

            public object GetFunctionPointerType(MethodSignature<object> signature) => null;

            public object GetGenericInstantiation(
                object genericType,
                ImmutableArray<object> typeArguments
            ) => null;

            public object GetGenericMethodParameter(object genericContext, int index) => null;

            public object GetGenericTypeParameter(object genericContext, int index) => null;

            public object GetModifiedType(
                object modifier,
                object unmodifiedType,
                bool isRequired
            ) => unmodifiedType;

            public object GetPinnedType(object elementType) => null;

            public object GetPointerType(object elementType) => null;

            public object GetPrimitiveType(PrimitiveTypeCode typeCode) => null;

            public object GetSZArrayType(object elementType) => null;

            public object GetTypeFromDefinition(
                MetadataReader reader,
                TypeDefinitionHandle handle,
                byte rawTypeKind
            )
            {
                Add(Resolve(handle));
                return null;
            }

            public object GetTypeFromReference(
                MetadataReader reader,
                TypeReferenceHandle handle,
                byte rawTypeKind
            )
            {
                Add(Resolve(handle));
                return null;
            }

            public object GetTypeFromSpecification(
                MetadataReader reader,
                object genericContext,
                TypeSpecificationHandle handle,
                byte rawTypeKind
            )
            {
                reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
                return null;
            }
        }

        private sealed class LoadedModule : IDisposable
        {
            private readonly FileStream stream;

            private LoadedModule(FileStream stream, PEReader pe, string assemblyName)
            {
                this.stream = stream;
                Pe = pe;
                Reader = pe.GetMetadataReader();
                AssemblyName = assemblyName;
            }

            public PEReader Pe { get; }

            public MetadataReader Reader { get; }

            public string AssemblyName { get; }

            public static LoadedModule Open(string path)
            {
                if (File.Exists(path) == false)
                {
                    throw new FileNotFoundException("Build output is missing.", path);
                }

                var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete
                );
                var pe = new PEReader(stream);
                MetadataReader reader = pe.GetMetadataReader();
                string name = reader.GetString(reader.GetAssemblyDefinition().Name);
                return new LoadedModule(stream, pe, name);
            }

            public void Dispose()
            {
                Pe.Dispose();
                stream.Dispose();
            }
        }
    }
}
