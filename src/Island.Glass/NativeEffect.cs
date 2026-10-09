using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Foundation;
using WinRT;

namespace Island.Glass;

/// <summary>
/// A hand-made COM object that stands in for a Direct2D effect description. The system compositor asks any effect it is given for
/// three interfaces: IGraphicsEffect, IGraphicsEffectSource and IGraphicsEffectD2D1Interop. Nothing in the Windows SDK projection
/// implements them (Win2D, a NuGet package, normally does), so this class builds the object by hand: one block of unmanaged memory
/// with three interface slots, each pointing at its own table of function pointers.
/// Method order of IGraphicsEffectD2D1Interop is taken from the Avalonia IDL (MIT); Microsoft's page lists the methods only
/// alphabetically, so the order is UNVERIFIED against the header and is proven only by the compositor accepting the effect.
/// Moved here from tools/BlurProbe (WO1 section 7), where it was first proven; the probe now uses this copy.
/// </summary>
internal static unsafe class NativeEffect
{
    public static readonly Guid ClsidGaussianBlur = new("1FEB6D69-2FE6-4AC9-8C58-1D7F93E7A6A5"); // CLSID_D2D1GaussianBlur
    // CLSID_D2D1ColorMatrix: the Learn page names the CLSID but not its value; this value is from d2d1effects.h as remembered,
    // UNVERIFIED there and proven only by the compositor accepting the effect (review/glass/glass.json, "tree_created").
    public static readonly Guid ClsidColorMatrix = new("921F03D6-641C-47DF-852D-B4BB6153AE11");

    /// <summary>The IGraphicsEffectSource face of an effect made by <see cref="Create"/>, for use as another effect's source.</summary>
    public static IntPtr AsSource(IntPtr effect) => effect + sizeof(void*);
    static readonly Guid IidUnknown = new("00000000-0000-0000-C000-000000000046");
    static readonly Guid IidInspectable = new("AF86E2E0-B12D-4C6A-9C5A-D7AA65101E90");
    static readonly Guid IidAgile = new("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90");
    public static readonly Guid IidGraphicsEffect = new("CB51C0CE-8FE6-4636-B202-861FAA07D8F3");
    public static readonly Guid IidGraphicsEffectSource = new("2D8F9DDC-4339-4EB9-9216-F9DEB75658A2");
    static readonly Guid IidInterop = new("2FC57384-A068-44D7-A331-30982FCF7177");
    static readonly Guid IidPropertyValue = new("4BD682DD-7554-40E9-9A9B-82654EDE7E62");
    const int S_OK = 0, E_NOINTERFACE = unchecked((int)0x80004002), E_NOTIMPL = unchecked((int)0x80004001), E_FAIL = unchecked((int)0x80004005), E_INVALIDARG = unchecked((int)0x80070057);

    sealed record Node(Guid Id, object[] Props, IntPtr[] Sources);

    [StructLayout(LayoutKind.Sequential)]
    struct Block { public void* V0; public void* V1; public void* V2; public long Refs; public IntPtr Handle; }

    static void** vtEffect, vtSource, vtInterop;

    /// <summary>Creates the effect object. Properties are boxed float, uint, bool or float[] values, in the Direct2D property order of the effect.</summary>
    public static IntPtr Create(Guid effectId, object[] props, IntPtr[] sources)
    {
        EnsureTables();
        var b = (Block*)NativeMemory.AllocZeroed((nuint)sizeof(Block));
        b->V0 = vtEffect; b->V1 = vtSource; b->V2 = vtInterop; b->Refs = 1;
        b->Handle = GCHandle.ToIntPtr(GCHandle.Alloc(new Node(effectId, props, sources)));
        // ponytail: the block is never freed. The compositor is known to release these pointers more often than it should. The app
        // makes two of them per glass layer (a few hundred bytes, once per run); a host that made layers in a loop would need a
        // proper lifetime story here.
        return (IntPtr)b;
    }

    static void EnsureTables()
    {
        if (vtEffect != null) return;
        vtEffect = (void**)NativeMemory.Alloc((nuint)(8 * sizeof(void*)));
        vtEffect[0] = (void*)(delegate* unmanaged[Stdcall]<void*, Guid*, void**, int>)&Qi0;
        vtEffect[1] = (void*)(delegate* unmanaged[Stdcall]<void*, uint>)&AddRef0;
        vtEffect[2] = (void*)(delegate* unmanaged[Stdcall]<void*, uint>)&Release0;
        vtEffect[3] = (void*)(delegate* unmanaged[Stdcall]<void*, uint*, Guid**, int>)&GetIids;
        vtEffect[4] = (void*)(delegate* unmanaged[Stdcall]<void*, IntPtr*, int>)&GetRuntimeClassName;
        vtEffect[5] = (void*)(delegate* unmanaged[Stdcall]<void*, int*, int>)&GetTrustLevel;
        vtEffect[6] = (void*)(delegate* unmanaged[Stdcall]<void*, IntPtr*, int>)&GetName;
        vtEffect[7] = (void*)(delegate* unmanaged[Stdcall]<void*, IntPtr, int>)&SetName;

        vtSource = (void**)NativeMemory.Alloc((nuint)(6 * sizeof(void*)));
        vtSource[0] = (void*)(delegate* unmanaged[Stdcall]<void*, Guid*, void**, int>)&Qi1;
        vtSource[1] = (void*)(delegate* unmanaged[Stdcall]<void*, uint>)&AddRef1;
        vtSource[2] = (void*)(delegate* unmanaged[Stdcall]<void*, uint>)&Release1;
        vtSource[3] = vtEffect[3]; vtSource[4] = vtEffect[4]; vtSource[5] = vtEffect[5];

        vtInterop = (void**)NativeMemory.Alloc((nuint)(9 * sizeof(void*)));
        vtInterop[0] = (void*)(delegate* unmanaged[Stdcall]<void*, Guid*, void**, int>)&Qi2;
        vtInterop[1] = (void*)(delegate* unmanaged[Stdcall]<void*, uint>)&AddRef2;
        vtInterop[2] = (void*)(delegate* unmanaged[Stdcall]<void*, uint>)&Release2;
        vtInterop[3] = (void*)(delegate* unmanaged[Stdcall]<void*, Guid*, int>)&GetEffectId;
        vtInterop[4] = (void*)(delegate* unmanaged[Stdcall]<void*, IntPtr, uint*, int*, int>)&GetNamedPropertyMapping;
        vtInterop[5] = (void*)(delegate* unmanaged[Stdcall]<void*, uint*, int>)&GetPropertyCount;
        vtInterop[6] = (void*)(delegate* unmanaged[Stdcall]<void*, uint, IntPtr*, int>)&GetProperty;
        vtInterop[7] = (void*)(delegate* unmanaged[Stdcall]<void*, uint, IntPtr*, int>)&GetSource;
        vtInterop[8] = (void*)(delegate* unmanaged[Stdcall]<void*, uint*, int>)&GetSourceCount;
    }

    static Block* BlockOf(void* self, int slot) => (Block*)((byte*)self - slot * sizeof(void*));
    static Node NodeOf(void* self, int slot) => (Node)GCHandle.FromIntPtr(BlockOf(self, slot)->Handle).Target!;

    static int Qi(void* self, int slot, Guid* riid, void** ppv)
    {
        int target;
        if (*riid == IidUnknown || *riid == IidInspectable || *riid == IidAgile || *riid == IidGraphicsEffect) target = 0;
        else if (*riid == IidGraphicsEffectSource) target = 1;
        else if (*riid == IidInterop) target = 2;
        else { *ppv = null; return E_NOINTERFACE; }
        var b = BlockOf(self, slot);
        Interlocked.Increment(ref b->Refs);
        *ppv = (byte*)b + target * sizeof(void*);
        return S_OK;
    }
    static uint AddRef(void* self, int slot) => (uint)Interlocked.Increment(ref BlockOf(self, slot)->Refs);
    static uint Release(void* self, int slot)
    {
        var b = BlockOf(self, slot);
        long n = Interlocked.Decrement(ref b->Refs);
        if (n < 1) Interlocked.Exchange(ref b->Refs, 1); // never reaches zero: see ponytail note in Create
        return (uint)Math.Max(n, 1);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })] static int Qi0(void* s, Guid* r, void** p) => Qi(s, 0, r, p);
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })] static int Qi1(void* s, Guid* r, void** p) => Qi(s, 1, r, p);
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })] static int Qi2(void* s, Guid* r, void** p) => Qi(s, 2, r, p);
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })] static uint AddRef0(void* s) => AddRef(s, 0);
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })] static uint AddRef1(void* s) => AddRef(s, 1);
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })] static uint AddRef2(void* s) => AddRef(s, 2);
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })] static uint Release0(void* s) => Release(s, 0);
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })] static uint Release1(void* s) => Release(s, 1);
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })] static uint Release2(void* s) => Release(s, 2);

    // IInspectable
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })] static int GetIids(void* s, uint* count, Guid** iids) { *count = 0; *iids = null; return S_OK; }
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })] static int GetRuntimeClassName(void* s, IntPtr* name) { *name = 0; return S_OK; }
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })] static int GetTrustLevel(void* s, int* level) { *level = 0; return S_OK; }
    // IGraphicsEffect
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })] static int GetName(void* s, IntPtr* name) { *name = 0; return S_OK; }
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })] static int SetName(void* s, IntPtr name) => S_OK;

    // IGraphicsEffectD2D1Interop
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    static int GetEffectId(void* s, Guid* id) { *id = NodeOf(s, 2).Id; return S_OK; }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    static int GetNamedPropertyMapping(void* s, IntPtr name, uint* index, int* mapping) => E_NOTIMPL;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    static int GetPropertyCount(void* s, uint* count) { *count = (uint)NodeOf(s, 2).Props.Length; return S_OK; }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    static int GetSourceCount(void* s, uint* count) { *count = (uint)NodeOf(s, 2).Sources.Length; return S_OK; }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    static int GetSource(void* s, uint index, IntPtr* source)
    {
        var n = NodeOf(s, 2);
        if (index >= n.Sources.Length) { *source = 0; return E_INVALIDARG; }
        Marshal.AddRef(n.Sources[index]);
        *source = n.Sources[index];
        return S_OK;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    static int GetProperty(void* s, uint index, IntPtr* value)
    {
        try
        {
            var n = NodeOf(s, 2);
            if (index >= n.Props.Length) { *value = 0; return E_INVALIDARG; }
            object pv = n.Props[index] switch
            {
                float f => PropertyValue.CreateSingle(f),
                uint u => PropertyValue.CreateUInt32(u),
                bool b => PropertyValue.CreateBoolean(b),
                float[] a => PropertyValue.CreateSingleArray(a),
                _ => throw new NotSupportedException()
            };
            var unk = MarshalInspectable<object>.FromManaged(pv);
            var iid = IidPropertyValue;
            int hr = Marshal.QueryInterface(unk, in iid, out var ptr);
            Marshal.Release(unk);
            *value = ptr;
            return hr;
        }
        catch { *value = 0; return E_FAIL; }
    }
}
