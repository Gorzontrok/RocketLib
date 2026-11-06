using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using dnlib.PE;

namespace RocketLib.Utils
{
    /// <summary>Checks a DLL's references into the given libraries from its bytes, without loading it.</summary>
    public sealed class ReferenceScanner : IDisposable
    {
        private readonly string[] libraryPaths;

        private readonly string[] gameAssemblyPaths;

        // A ScanModules, typed object so this class still loads when dnlib is missing or changed.
        private object modules;
        private bool disposed;

        /// <summary>Stores the paths; a null or missing library path skips that library, and nothing opens until the first scan.</summary>
        public ReferenceScanner(string[] libraryPaths, string[] gameAssemblyPaths)
        {
            this.libraryPaths = libraryPaths ?? new string[0];
            this.gameAssemblyPaths = gameAssemblyPaths ?? new string[0];
        }

        /// <summary>Scans one DLL and never throws; a scan that can't finish returns a result with Checked false.</summary>
        public ReferenceScanResult Scan(string dllPath)
        {
            if (disposed)
            {
                return ReferenceScanResult.Failed("ObjectDisposedException: this scanner was disposed", false);
            }

            byte[] image;
            try
            {
                image = File.ReadAllBytes(dllPath);
            }
            catch (Exception ex)
            {
                return ReferenceScanResult.Failed(ex, true);
            }

            try
            {
                return ScanImage(image);
            }
            catch (Exception ex)
            {
                return ReferenceScanResult.Failed(ex, false);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal ReferenceScanResult ScanImage(byte[] image)
        {
            ScanModules opened = modules as ScanModules;
            if (opened == null)
            {
                opened = new ScanModules(libraryPaths, gameAssemblyPaths);
                modules = opened;
            }
            return new ScanRun(opened).Run(image);
        }

        /// <summary>Closes the cached library and game modules.</summary>
        public void Dispose()
        {
            disposed = true;
            IDisposable opened = modules as IDisposable;
            modules = null;
            if (opened != null)
            {
                opened.Dispose();
            }
        }
    }

    /// <summary>The outcome of scanning one DLL.</summary>
    public sealed class ReferenceScanResult
    {
        private static readonly ReadOnlyCollection<MissingReference> NoMissing = new List<MissingReference>().AsReadOnly();
        private static readonly ReadOnlyCollection<string> NoUnresolved = new List<string>().AsReadOnly();

        private readonly Dictionary<string, Version> referenced;

        internal ReferenceScanResult(List<MissingReference> missing, List<string> unresolved, Dictionary<string, Version> referenced)
        {
            Checked = true;
            Missing = missing.AsReadOnly();
            Unresolved = unresolved.AsReadOnly();
            this.referenced = referenced;
        }

        private ReferenceScanResult(string reason, bool unreadable)
        {
            UncheckedReason = reason;
            Unreadable = unreadable;
            Missing = NoMissing;
            Unresolved = NoUnresolved;
            referenced = new Dictionary<string, Version>();
        }

        internal static ReferenceScanResult Failed(string reason, bool unreadable)
        {
            return new ReferenceScanResult(reason, unreadable);
        }

        internal static ReferenceScanResult Failed(Exception ex, bool unreadable)
        {
            return Failed(ex.GetType().Name + ": " + ex.Message, unreadable);
        }

        /// <summary>True when the scan finished.</summary>
        public bool Checked { get; }

        /// <summary>True when the scanned DLL itself couldn't be read, as opposed to the scanner failing.</summary>
        public bool Unreadable { get; }

        /// <summary>Why the scan didn't finish, as ExceptionType: message.</summary>
        public string UncheckedReason { get; }

        /// <summary>The references the libraries lack, sorted by library, types first, then target.</summary>
        public ReadOnlyCollection<MissingReference> Missing { get; }

        /// <summary>References whose chain ran into a link that couldn't be resolved, each with that link.</summary>
        public ReadOnlyCollection<string> Unresolved { get; }

        /// <summary>The three-part version of the DLL's reference to the named assembly, or null when it has none.</summary>
        public Version ReferencedVersion(string library)
        {
            Version version;
            return library != null && referenced.TryGetValue(library, out version) ? version : null;
        }
    }

    /// <summary>One library type or member a scanned DLL references that the library doesn't have.</summary>
    public sealed class MissingReference
    {
        private const int MaxSites = 3;

        private readonly List<string> usedIn = new List<string>();

        internal MissingReference(string library, string target, string display, bool isType)
        {
            Library = library;
            Target = target;
            Display = display;
            IsType = isType;
            UsedIn = usedIn.AsReadOnly();
        }

        /// <summary>The library's assembly name.</summary>
        public string Library { get; }

        /// <summary>The stable full form of the reference, without the library name.</summary>
        public string Target { get; }

        /// <summary>The short form of the reference, for players.</summary>
        public string Display { get; }

        /// <summary>True for a missing type, false for a missing member.</summary>
        public bool IsType { get; }

        /// <summary>Up to three Type.Method call sites, compiler-generated names cleaned.</summary>
        public ReadOnlyCollection<string> UsedIn { get; }

        internal void AddSite(string site)
        {
            if (usedIn.Count < MaxSites && !usedIn.Contains(site))
            {
                usedIn.Add(site);
            }
        }
    }

    internal sealed class ScanModules : IDisposable
    {
        private readonly string[] libraryPaths;
        private readonly string[] gameAssemblyPaths;
        private readonly List<IDisposable> opened = new List<IDisposable>();
        private Dictionary<string, Dictionary<string, TypeDef>> libraries;
        private Exception libraryFailure;
        private List<Dictionary<string, TypeDef>> gameModules;

        internal ScanModules(string[] libraryPaths, string[] gameAssemblyPaths)
        {
            this.libraryPaths = libraryPaths;
            this.gameAssemblyPaths = gameAssemblyPaths;
        }

        internal Dictionary<string, Dictionary<string, TypeDef>> Libraries()
        {
            if (libraryFailure != null)
            {
                throw libraryFailure;
            }
            if (libraries != null)
            {
                return libraries;
            }

            Dictionary<string, Dictionary<string, TypeDef>> opening = new Dictionary<string, Dictionary<string, TypeDef>>(StringComparer.Ordinal);
            try
            {
                foreach (string path in libraryPaths)
                {
                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    {
                        ModuleDefMD module = Open(path);
                        opening[module.Assembly.Name.String] = Index(module);
                    }
                }
            }
            catch (Exception ex)
            {
                libraryFailure = ex;
                throw;
            }
            libraries = opening;
            return libraries;
        }

        internal TypeDef Find(string assemblyName, string fullName)
        {
            Dictionary<string, TypeDef> types;
            TypeDef found;
            if (assemblyName != null && libraries.TryGetValue(assemblyName, out types) && types.TryGetValue(fullName, out found))
            {
                return found;
            }
            foreach (Dictionary<string, TypeDef> game in GameModules())
            {
                if (game.TryGetValue(fullName, out found))
                {
                    return found;
                }
            }
            return null;
        }

        public void Dispose()
        {
            foreach (IDisposable module in opened)
            {
                module.Dispose();
            }
            opened.Clear();
        }

        private List<Dictionary<string, TypeDef>> GameModules()
        {
            if (gameModules != null)
            {
                return gameModules;
            }

            gameModules = new List<Dictionary<string, TypeDef>>();
            foreach (string path in gameAssemblyPaths)
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    continue;
                }
                try
                {
                    gameModules.Add(Index(Open(path)));
                }
                catch (Exception)
                {
                }
            }
            return gameModules;
        }

        private ModuleDefMD Open(string path)
        {
            ModuleDefMD module = ModuleDefMD.Load(File.ReadAllBytes(path));
            opened.Add(module);
            return module;
        }

        private static Dictionary<string, TypeDef> Index(ModuleDefMD module)
        {
            Dictionary<string, TypeDef> types = new Dictionary<string, TypeDef>(StringComparer.Ordinal);
            foreach (TypeDef type in module.GetTypes())
            {
                types[type.FullName] = type;
            }
            return types;
        }
    }

    internal sealed class ScanRun
    {
        private readonly ScanModules modules;
        private readonly Dictionary<string, MissingReference> missing = new Dictionary<string, MissingReference>(StringComparer.Ordinal);
        private readonly Dictionary<uint, MissingReference> byToken = new Dictionary<uint, MissingReference>();
        private readonly List<string> unresolved = new List<string>();
        private Dictionary<string, Dictionary<string, TypeDef>> libraries;
        private string scannedAssembly;

        internal ScanRun(ScanModules modules)
        {
            this.modules = modules;
        }

        internal ReferenceScanResult Run(byte[] image)
        {
            libraries = modules.Libraries();

            ModuleDefMD module = null;
            try
            {
                Dictionary<string, Version> referenced = new Dictionary<string, Version>(StringComparer.Ordinal);
                List<TypeRef> typeRefs = new List<TypeRef>();
                List<MemberRef> memberRefs = new List<MemberRef>();
                try
                {
                    module = ModuleDefMD.Load(image);
                    RejectTruncated(module, image.Length);
                    scannedAssembly = module.Assembly == null ? null : module.Assembly.Name.String;
                    ReadAssemblyRefs(module, referenced);
                    typeRefs.AddRange(module.GetTypeRefs());
                    memberRefs.AddRange(module.GetMemberRefs());
                }
                catch (Exception ex)
                {
                    if (IsScannerFailure(ex))
                    {
                        throw;
                    }
                    return ReferenceScanResult.Failed(ex, true);
                }

                foreach (TypeRef typeRef in typeRefs)
                {
                    CheckType(typeRef);
                }
                foreach (MemberRef memberRef in memberRefs)
                {
                    CheckMember(memberRef);
                }

                if (missing.Count > 0)
                {
                    try
                    {
                        CollectSites(module);
                    }
                    catch (Exception ex)
                    {
                        if (IsScannerFailure(ex))
                        {
                            throw;
                        }
                        return ReferenceScanResult.Failed(ex, true);
                    }
                }

                List<MissingReference> sorted = new List<MissingReference>(missing.Values);
                sorted.Sort(CompareMissing);
                unresolved.Sort(StringComparer.Ordinal);
                return new ReferenceScanResult(sorted, unresolved, referenced);
            }
            finally
            {
                if (module != null)
                {
                    module.Dispose();
                }
            }
        }

        private static bool IsScannerFailure(Exception ex)
        {
            return ex is TypeLoadException || ex is MemberAccessException || ex is FileNotFoundException
                   || ex is FileLoadException || ex is TypeInitializationException;
        }

        // dnlib only reads what the metadata points at, so it accepts a file cut off after the metadata.
        private static void RejectTruncated(ModuleDefMD module, int imageLength)
        {
            foreach (ImageSectionHeader section in module.Metadata.PEImage.ImageSectionHeaders)
            {
                if ((long)section.PointerToRawData + section.SizeOfRawData > imageLength)
                {
                    throw new BadImageFormatException("The file ends before its last section");
                }
            }
        }

        private static void ReadAssemblyRefs(ModuleDefMD module, Dictionary<string, Version> referenced)
        {
            foreach (AssemblyRef assemblyRef in module.GetAssemblyRefs())
            {
                Version full = assemblyRef.Version;
                Version version = new Version(full.Major, full.Minor, System.Math.Max(full.Build, 0));
                Version existing;
                if (!referenced.TryGetValue(assemblyRef.Name.String, out existing) || version > existing)
                {
                    referenced[assemblyRef.Name.String] = version;
                }
            }
        }

        private static int CompareMissing(MissingReference a, MissingReference b)
        {
            int order = string.CompareOrdinal(a.Library, b.Library);
            if (order == 0)
            {
                order = b.IsType.CompareTo(a.IsType);
            }
            return order != 0 ? order : string.CompareOrdinal(a.Target, b.Target);
        }

        private bool TryGetLibrary(TypeRef type, out Dictionary<string, TypeDef> types)
        {
            AssemblyRef assembly = type.DefinitionAssembly as AssemblyRef;
            types = null;
            return assembly != null && libraries.TryGetValue(assembly.Name.String, out types);
        }

        private void CheckType(TypeRef typeRef)
        {
            Dictionary<string, TypeDef> types;
            if (TryGetLibrary(typeRef, out types) && !types.ContainsKey(typeRef.FullName))
            {
                AddMissingType(typeRef);
            }
        }

        private MissingReference AddMissingType(TypeRef typeRef)
        {
            MissingReference entry = AddMissing(typeRef, typeRef.FullName, ScanText.TypeDisplay(typeRef), true);
            byToken[typeRef.MDToken.Raw] = entry;
            return entry;
        }

        private MissingReference AddMissing(TypeRef declaring, string target, string display, bool isType)
        {
            string library = declaring.DefinitionAssembly.Name.String;
            string key = library + ":" + target;
            MissingReference entry;
            if (!missing.TryGetValue(key, out entry))
            {
                entry = new MissingReference(library, target, display, isType);
                missing[key] = entry;
            }
            return entry;
        }

        private void CheckMember(MemberRef reference)
        {
            GenericInstSig instance = reference.Class is TypeSpec ? ((TypeSpec)reference.Class).TypeSig as GenericInstSig : null;
            TypeRef declaring = (instance != null ? instance.GenericType.TypeDefOrRef : reference.Class) as TypeRef;
            Dictionary<string, TypeDef> types;
            if (declaring == null || !TryGetLibrary(declaring, out types))
            {
                return;
            }

            TypeDef start;
            if (!types.TryGetValue(declaring.FullName, out start))
            {
                byToken[reference.MDToken.Raw] = AddMissingType(declaring);
                return;
            }

            if ((reference.MethodSig == null && reference.FieldSig == null) || (reference.MethodSig != null && reference.MethodSig.IsVarArg))
            {
                return;
            }

            MemberLookup lookup = new MemberLookup(modules, reference, scannedAssembly);
            if (lookup.Find(start))
            {
                return;
            }

            string target = declaring.FullName + "::" + ScanText.MemberKey(reference, null);
            if (lookup.BrokenLink != null)
            {
                string line = target + " (via " + lookup.BrokenLink + ")";
                if (!unresolved.Contains(line))
                {
                    unresolved.Add(line);
                }
                return;
            }
            byToken[reference.MDToken.Raw] = AddMissing(declaring, target, ScanText.MemberDisplay(reference, declaring, instance), false);
        }

        private void CollectSites(ModuleDefMD module)
        {
            foreach (TypeDef type in module.GetTypes())
            {
                foreach (MethodDef method in type.Methods)
                {
                    if (!method.HasBody)
                    {
                        continue;
                    }
                    string site = null;
                    foreach (Instruction instruction in method.Body.Instructions)
                    {
                        MissingReference entry = EntryFor(instruction.Operand);
                        if (entry != null)
                        {
                            site = site ?? ScanText.Site(type, method);
                            entry.AddSite(site);
                        }
                    }
                }
            }
        }

        private MissingReference EntryFor(object operand)
        {
            MethodSpec methodSpec = operand as MethodSpec;
            if (methodSpec != null)
            {
                operand = methodSpec.Method;
            }
            TypeSpec typeSpec = operand as TypeSpec;
            if (typeSpec != null)
            {
                operand = typeSpec.ScopeType;
            }

            MissingReference entry;
            if ((operand is MemberRef || operand is TypeRef) && byToken.TryGetValue(((IMDTokenProvider)operand).MDToken.Raw, out entry))
            {
                return entry;
            }
            return null;
        }
    }

    internal sealed class MemberLookup
    {
        private const string SystemPrefix = "System.";

        private readonly ScanModules modules;
        private readonly MemberRef reference;
        private readonly string scannedAssembly;
        private readonly HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        private string rawKey;

        internal MemberLookup(ScanModules modules, MemberRef reference, string scannedAssembly)
        {
            this.modules = modules;
            this.reference = reference;
            this.scannedAssembly = scannedAssembly;
        }

        internal string BrokenLink { get; private set; }

        internal bool Find(TypeDef start)
        {
            List<KeyValuePair<ITypeDefOrRef, IList<string>>> interfaces = new List<KeyValuePair<ITypeDefOrRef, IList<string>>>();
            bool isConstructor = reference.Name == ".ctor" || reference.Name == ".cctor";
            TypeDef type = start;
            IList<string> args = null;
            while (type != null)
            {
                bool accessible;
                if (TryMatch(type, args, out accessible))
                {
                    return accessible;
                }
                if (isConstructor)
                {
                    return false;
                }
                AddInterfaces(type, args, interfaces);
                type = Follow(type.BaseType, ref args);
            }

            for (int i = 0; i < interfaces.Count; i++)
            {
                IList<string> interfaceArgs = interfaces[i].Value;
                TypeDef candidate = Follow(interfaces[i].Key, ref interfaceArgs);
                if (candidate == null)
                {
                    continue;
                }
                bool accessible;
                if (TryMatch(candidate, interfaceArgs, out accessible))
                {
                    return accessible;
                }
                AddInterfaces(candidate, interfaceArgs, interfaces);
            }

            return reference.IsMethodRef && IsObjectMember(RawKey());
        }

        private static void AddInterfaces(TypeDef type, IList<string> args, List<KeyValuePair<ITypeDefOrRef, IList<string>>> interfaces)
        {
            foreach (InterfaceImpl implemented in type.Interfaces)
            {
                interfaces.Add(new KeyValuePair<ITypeDefOrRef, IList<string>>(implemented.Interface, args));
            }
        }

        // Base and interface links resolve by full name, since a ref may name the UnityEngine facade while the type lives in a module.
        private TypeDef Follow(ITypeDefOrRef link, ref IList<string> args)
        {
            if (link == null || !seen.Add(link.FullName))
            {
                return null;
            }

            ITypeDefOrRef declared = link;
            IList<string> linkArgs = null;
            if (link is TypeSpec)
            {
                GenericInstSig instance = ((TypeSpec)link).TypeSig as GenericInstSig;
                if (instance == null)
                {
                    BrokenLink = BrokenLink ?? link.FullName;
                    return null;
                }
                declared = instance.GenericType.TypeDefOrRef;
                linkArgs = ScanText.RenderAll(instance.GenericArguments, args);
            }
            args = linkArgs;

            TypeDef definition = declared as TypeDef;
            if (definition != null)
            {
                return definition;
            }

            string name = declared.FullName;
            if (name.StartsWith(SystemPrefix, StringComparison.Ordinal))
            {
                return null;
            }

            IAssembly assembly = declared.DefinitionAssembly;
            definition = modules.Find(assembly == null ? null : assembly.Name.String, name);
            if (definition == null)
            {
                BrokenLink = BrokenLink ?? name;
            }
            return definition;
        }

        private bool TryMatch(TypeDef type, IList<string> args, out bool accessible)
        {
            accessible = false;
            if (reference.IsFieldRef)
            {
                foreach (FieldDef field in type.Fields)
                {
                    if (field.Name == reference.Name && Matches(ScanText.FieldKey(field.Name, field.FieldSig, args), ScanText.FieldKey(field.Name, field.FieldSig, null)))
                    {
                        accessible = field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly
                                     || ((field.IsAssembly || field.IsFamilyAndAssembly) && GrantsInternals(type));
                        return true;
                    }
                }
                return false;
            }

            foreach (MethodDef method in type.Methods)
            {
                if (method.Name == reference.Name && Matches(ScanText.MethodKey(method.Name, method.MethodSig, args), ScanText.MethodKey(method.Name, method.MethodSig, null)))
                {
                    accessible = method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly
                                 || ((method.IsAssembly || method.IsFamilyAndAssembly) && GrantsInternals(type));
                    return true;
                }
            }
            return false;
        }

        private bool Matches(string substituted, string raw)
        {
            string wanted = RawKey();
            return substituted == wanted || raw == wanted;
        }

        private string RawKey()
        {
            rawKey = rawKey ?? ScanText.MemberKey(reference, null);
            return rawKey;
        }

        private bool GrantsInternals(TypeDef type)
        {
            AssemblyDef assembly = type.Module.Assembly;
            if (assembly == null || scannedAssembly == null)
            {
                return false;
            }

            foreach (CustomAttribute attribute in assembly.CustomAttributes)
            {
                if (attribute.TypeFullName != "System.Runtime.CompilerServices.InternalsVisibleToAttribute" || attribute.ConstructorArguments.Count != 1)
                {
                    continue;
                }
                UTF8String granted = attribute.ConstructorArguments[0].Value as UTF8String;
                if ((object)granted != null && string.Equals(granted.String.Split(',')[0].Trim(), scannedAssembly, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsObjectMember(string key)
        {
            switch (key)
            {
                case "Equals(System.Object)System.Boolean":
                case "static Equals(System.Object,System.Object)System.Boolean":
                case "GetHashCode()System.Int32":
                case "GetType()System.Type":
                case "ToString()System.String":
                case "MemberwiseClone()System.Object":
                case "static ReferenceEquals(System.Object,System.Object)System.Boolean":
                case "Finalize()System.Void":
                    return true;
                default:
                    return false;
            }
        }
    }

    internal static class ScanText
    {
        internal static string MemberKey(MemberRef reference, IList<string> args)
        {
            return reference.IsFieldRef ? FieldKey(reference.Name, reference.FieldSig, args) : MethodKey(reference.Name, reference.MethodSig, args);
        }

        internal static string FieldKey(UTF8String name, FieldSig sig, IList<string> args)
        {
            return name.String + ":" + Render(sig.Type, args, false);
        }

        internal static string MethodKey(UTF8String name, MethodSig sig, IList<string> args)
        {
            return (sig.HasThis ? string.Empty : "static ") + name.String + (sig.GenParamCount > 0 ? "`" + sig.GenParamCount : string.Empty)
                   + "(" + string.Join(",", RenderAll(sig.Params, args)) + ")" + Render(sig.RetType, args, false);
        }

        internal static string[] RenderAll(IList<TypeSig> sigs, IList<string> args, bool brief = false)
        {
            string[] rendered = new string[sigs.Count];
            for (int i = 0; i < rendered.Length; i++)
            {
                rendered[i] = Render(sigs[i], args, brief);
            }
            return rendered;
        }

        private static string Render(TypeSig sig, IList<string> args, bool brief)
        {
            if (sig == null)
            {
                return string.Empty;
            }

            GenericVar classVar = sig as GenericVar;
            if (classVar != null)
            {
                if (args != null && classVar.Number < args.Count)
                {
                    return args[(int)classVar.Number];
                }
                return brief ? "T" : "!" + classVar.Number;
            }
            GenericMVar methodVar = sig as GenericMVar;
            if (methodVar != null)
            {
                return brief ? methodVar.Number == 0 ? "T" : "T" + (methodVar.Number + 1) : "!!" + methodVar.Number;
            }
            SZArraySig szArray = sig as SZArraySig;
            if (szArray != null)
            {
                return Render(szArray.Next, args, brief) + "[]";
            }
            ArraySig array = sig as ArraySig;
            if (array != null)
            {
                return Render(array.Next, args, brief) + "[" + new string(',', (int)System.Math.Max(1, array.Rank) - 1) + "]";
            }
            ByRefSig byRef = sig as ByRefSig;
            if (byRef != null)
            {
                return brief ? "ref " + Render(byRef.Next, args, true) : Render(byRef.Next, args, false) + "&";
            }
            PtrSig pointer = sig as PtrSig;
            if (pointer != null)
            {
                return Render(pointer.Next, args, brief) + "*";
            }
            if (sig is ModifierSig || sig is PinnedSig)
            {
                return Render(sig.Next, args, brief);
            }
            GenericInstSig instance = sig as GenericInstSig;
            if (instance != null)
            {
                string name = instance.GenericType.TypeDefOrRef.FullName;
                string separator = brief ? ", " : ",";
                int arity;
                return (brief ? ShortName(name, out arity) : name) + "<" + string.Join(separator, RenderAll(instance.GenericArguments, args, brief)) + ">";
            }
            TypeDefOrRefSig plain = sig as TypeDefOrRefSig;
            if (plain != null)
            {
                return brief ? BriefName(plain.TypeDefOrRef.FullName) : plain.TypeDefOrRef.FullName;
            }
            return sig.FullName;
        }

        private static string BriefName(string fullName)
        {
            switch (fullName)
            {
                case "System.Void": return "void";
                case "System.Boolean": return "bool";
                case "System.Byte": return "byte";
                case "System.SByte": return "sbyte";
                case "System.Char": return "char";
                case "System.Int16": return "short";
                case "System.UInt16": return "ushort";
                case "System.Int32": return "int";
                case "System.UInt32": return "uint";
                case "System.Int64": return "long";
                case "System.UInt64": return "ulong";
                case "System.Single": return "float";
                case "System.Double": return "double";
                case "System.Decimal": return "decimal";
                case "System.String": return "string";
                case "System.Object": return "object";
            }
            int arity;
            return ShortName(fullName, out arity);
        }

        private static string ShortName(string fullName, out int arity)
        {
            int nested = fullName.IndexOf('/');
            int start = fullName.LastIndexOf('.', nested < 0 ? fullName.Length - 1 : nested) + 1;
            arity = 0;

            StringBuilder name = new StringBuilder(fullName.Length - start);
            for (int i = start; i < fullName.Length; i++)
            {
                if (fullName[i] != '`')
                {
                    name.Append(fullName[i] == '/' ? '.' : fullName[i]);
                    continue;
                }
                int count = 0;
                while (i + 1 < fullName.Length && char.IsDigit(fullName[i + 1]))
                {
                    count = (count * 10) + (fullName[++i] - '0');
                }
                arity += count;
            }
            return name.ToString();
        }

        internal static string TypeDisplay(ITypeDefOrRef type)
        {
            int arity;
            string name = ShortName(type.FullName, out arity);
            if (arity == 0)
            {
                return name;
            }
            if (arity == 1)
            {
                return name + "<T>";
            }

            string[] parameters = new string[arity];
            for (int i = 0; i < arity; i++)
            {
                parameters[i] = "T" + (i + 1);
            }
            return name + "<" + string.Join(", ", parameters) + ">";
        }

        internal static string MemberDisplay(MemberRef reference, TypeRef declaring, GenericInstSig instance)
        {
            string type = instance != null ? Render(instance, null, true) : TypeDisplay(declaring);
            string name = reference.Name.String;
            if (reference.IsFieldRef)
            {
                return type + "." + name;
            }

            foreach (string prefix in new[] { "get_", "set_", "add_", "remove_" })
            {
                if (name.Length > prefix.Length && name.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return type + "." + name.Substring(prefix.Length);
                }
            }

            string[] args = instance == null ? null : RenderAll(instance.GenericArguments, null, true);
            string parameters = "(" + string.Join(", ", RenderAll(reference.MethodSig.Params, args, true)) + ")";
            if (name == ".ctor")
            {
                return "new " + type + parameters;
            }

            uint generics = reference.MethodSig.GenParamCount;
            if (generics == 0)
            {
                return type + "." + name + parameters;
            }
            string[] typeParameters = new string[generics];
            for (int i = 0; i < generics; i++)
            {
                typeParameters[i] = generics == 1 ? "T" : "T" + (i + 1);
            }
            return type + "." + name + "<" + string.Join(", ", typeParameters) + ">" + parameters;
        }

        internal static string Site(TypeDef type, MethodDef method)
        {
            string name = method.Name.String;
            if (name.StartsWith("<", StringComparison.Ordinal))
            {
                name = GeneratedFrom(name);
            }

            while (type.DeclaringType != null && type.Name.String.StartsWith("<", StringComparison.Ordinal))
            {
                string origin = GeneratedFrom(type.Name.String);
                if (origin.Length > 0)
                {
                    name = origin;
                }
                type = type.DeclaringType;
            }

            int arity;
            string owner = ShortName(type.FullName, out arity);
            name = name.TrimStart('.');
            return name.Length > 0 ? owner + "." + name : owner;
        }

        private static string GeneratedFrom(string name)
        {
            int close = name.IndexOf('>');
            return close > 1 ? name.Substring(1, close - 1) : string.Empty;
        }
    }
}
