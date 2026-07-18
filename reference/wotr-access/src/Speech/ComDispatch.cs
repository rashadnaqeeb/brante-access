using System;
using System.Runtime.InteropServices;

namespace WrathAccess.Speech
{
    /// <summary>
    /// A fully MANUAL IDispatch client: CoCreateInstance via P/Invoke, methods invoked through raw
    /// vtable function pointers, VARIANTs marshalled by hand. Exists because Unity's Mono implements
    /// neither managed COM activation (Activator.CreateInstance on a ProgID type throws
    /// "Unmanaged activation is not supported") nor __ComObject late binding — but plain P/Invoke and
    /// Marshal.GetDelegateForFunctionPointer work everywhere. Supports exactly what driving SAPI's
    /// automation objects needs: name→DISPID lookup, property get/put/putref, method calls, and
    /// VARIANT payloads of int / bool / string / IDispatch / byte-array (SAFEARRAY).
    /// </summary>
    internal sealed class ComDispatch : IDisposable
    {
        // ---- ole32 / oleaut32 ----

        [DllImport("ole32.dll")]
        private static extern int CLSIDFromProgID([MarshalAs(UnmanagedType.LPWStr)] string progId, out Guid clsid);

        [DllImport("ole32.dll")]
        private static extern int CoCreateInstance(ref Guid clsid, IntPtr pUnkOuter, uint dwClsContext, ref Guid riid, out IntPtr ppv);

        [DllImport("ole32.dll")]
        private static extern int CoInitializeEx(IntPtr reserved, uint dwCoInit);

        [DllImport("oleaut32.dll")]
        private static extern void SysFreeString(IntPtr bstr);

        [DllImport("oleaut32.dll")]
        private static extern int SafeArrayGetLBound(IntPtr sa, uint dim, out int bound);

        [DllImport("oleaut32.dll")]
        private static extern int SafeArrayGetUBound(IntPtr sa, uint dim, out int bound);

        [DllImport("oleaut32.dll")]
        private static extern int SafeArrayAccessData(IntPtr sa, out IntPtr data);

        [DllImport("oleaut32.dll")]
        private static extern int SafeArrayUnaccessData(IntPtr sa);

        [DllImport("oleaut32.dll")]
        private static extern int SafeArrayDestroy(IntPtr sa);

        private const uint CLSCTX_INPROC_SERVER = 1;
        private const uint COINIT_APARTMENTTHREADED = 2;
        private const int DISPID_PROPERTYPUT = -3;
        private const ushort DISPATCH_METHOD = 1;
        private const ushort DISPATCH_PROPERTYGET = 2;
        private const ushort DISPATCH_PROPERTYPUT = 4;
        private const ushort DISPATCH_PROPERTYPUTREF = 8;

        // VARIANT type tags we handle.
        private const ushort VT_EMPTY = 0;
        private const ushort VT_I2 = 2;
        private const ushort VT_I4 = 3;
        private const ushort VT_BSTR = 8;
        private const ushort VT_DISPATCH = 9;
        private const ushort VT_BOOL = 11;
        private const ushort VT_UNKNOWN = 13;
        private const ushort VT_UI1 = 17;
        private const ushort VT_ARRAY = 0x2000;

        private const int VariantSize = 24; // x64 VARIANT: 8-byte header + 16-byte payload union
        private static Guid IID_IDispatch = new Guid("00020400-0000-0000-C000-000000000046");
        private static Guid IID_NULL = Guid.Empty;

        // IDispatch vtable: 0..2 IUnknown, 3 GetTypeInfoCount, 4 GetTypeInfo, 5 GetIDsOfNames, 6 Invoke.
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int ReleaseDel(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetIDsOfNamesDel(IntPtr self, ref Guid riid, ref IntPtr name, uint cNames, uint lcid, out int dispId);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int InvokeDel(IntPtr self, int dispId, ref Guid riid, uint lcid, ushort flags,
            ref DISPPARAMS dispParams, IntPtr varResult, IntPtr excepInfo, IntPtr argErr);

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPPARAMS
        {
            public IntPtr rgvarg;            // args, REVERSE order
            public IntPtr rgdispidNamedArgs; // for property puts: a single DISPID_PROPERTYPUT
            public uint cArgs;
            public uint cNamedArgs;
        }

        private IntPtr _ptr;
        private readonly ReleaseDel _release;
        private readonly GetIDsOfNamesDel _getIds;
        private readonly InvokeDel _invoke;

        private ComDispatch(IntPtr ptr)
        {
            _ptr = ptr;
            var vtbl = Marshal.ReadIntPtr(ptr);
            _release = Fn<ReleaseDel>(vtbl, 2);
            _getIds = Fn<GetIDsOfNamesDel>(vtbl, 5);
            _invoke = Fn<InvokeDel>(vtbl, 6);
        }

        private static T Fn<T>(IntPtr vtbl, int slot) where T : class =>
            (T)(object)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(vtbl, slot * IntPtr.Size), typeof(T));

        /// <summary>Create a COM object by ProgID and wrap its IDispatch. Null if unavailable.</summary>
        public static ComDispatch Create(string progId)
        {
            CoInitializeEx(IntPtr.Zero, COINIT_APARTMENTTHREADED); // RPC_E_CHANGED_MODE is fine — already initialized
            if (CLSIDFromProgID(progId, out var clsid) != 0) return null;
            if (CoCreateInstance(ref clsid, IntPtr.Zero, CLSCTX_INPROC_SERVER, ref IID_IDispatch, out var ptr) != 0
                || ptr == IntPtr.Zero) return null;
            return new ComDispatch(ptr);
        }

        public void Dispose()
        {
            if (_ptr != IntPtr.Zero)
            {
                try { _release(_ptr); } catch { }
                _ptr = IntPtr.Zero;
            }
        }

        // ---- the public surface ----

        public object Get(string name) => InvokeMember(name, DISPATCH_PROPERTYGET, null, true);
        public void Set(string name, object value) => InvokeMember(name, DISPATCH_PROPERTYPUT, new[] { value }, false);
        /// <summary>Property put BY REFERENCE — required for object-valued SAPI properties (Voice, AudioOutputStream).</summary>
        public void SetRef(string name, ComDispatch value) => InvokeMember(name, DISPATCH_PROPERTYPUTREF, new object[] { value }, false);
        public object Call(string name, params object[] args) => InvokeMember(name, DISPATCH_METHOD, args, true);

        private object InvokeMember(string name, ushort flags, object[] args, bool wantResult)
        {
            int dispId = GetDispId(name);
            int argCount = args?.Length ?? 0;

            IntPtr argMem = IntPtr.Zero, resultMem = IntPtr.Zero, namedMem = IntPtr.Zero, excepMem = IntPtr.Zero;
            var bstrs = new System.Collections.Generic.List<IntPtr>();
            try
            {
                if (argCount > 0)
                {
                    argMem = Marshal.AllocCoTaskMem(VariantSize * argCount);
                    for (int i = 0; i < argCount; i++) // rgvarg is reverse-ordered
                        WriteVariant(argMem + VariantSize * (argCount - 1 - i), args[i], bstrs);
                }
                var dp = new DISPPARAMS { rgvarg = argMem, cArgs = (uint)argCount };
                if ((flags & (DISPATCH_PROPERTYPUT | DISPATCH_PROPERTYPUTREF)) != 0)
                {
                    namedMem = Marshal.AllocCoTaskMem(4);
                    Marshal.WriteInt32(namedMem, DISPID_PROPERTYPUT);
                    dp.rgdispidNamedArgs = namedMem;
                    dp.cNamedArgs = 1;
                }
                if (wantResult)
                {
                    resultMem = Marshal.AllocCoTaskMem(VariantSize);
                    Zero(resultMem, VariantSize);
                }
                excepMem = Marshal.AllocCoTaskMem(112); // EXCEPINFO scratch (avoid faults on DISP_E_EXCEPTION)
                Zero(excepMem, 112);

                int hr = _invoke(_ptr, dispId, ref IID_NULL, 0, flags, ref dp, resultMem, excepMem, IntPtr.Zero);
                if (hr != 0)
                    throw new InvalidOperationException(name + " failed (hr=0x" + hr.ToString("X8") + ")");

                return wantResult ? ReadVariant(resultMem) : null;
            }
            finally
            {
                foreach (var b in bstrs) SysFreeString(b);
                if (argMem != IntPtr.Zero) Marshal.FreeCoTaskMem(argMem);
                if (resultMem != IntPtr.Zero) Marshal.FreeCoTaskMem(resultMem);
                if (namedMem != IntPtr.Zero) Marshal.FreeCoTaskMem(namedMem);
                if (excepMem != IntPtr.Zero) Marshal.FreeCoTaskMem(excepMem);
            }
        }

        private int GetDispId(string name)
        {
            var namePtr = Marshal.StringToCoTaskMemUni(name);
            try
            {
                int hr = _getIds(_ptr, ref IID_NULL, ref namePtr, 1, 0, out int dispId);
                if (hr != 0) throw new InvalidOperationException("Unknown member: " + name + " (hr=0x" + hr.ToString("X8") + ")");
                return dispId;
            }
            finally { Marshal.FreeCoTaskMem(namePtr); }
        }

        private static void Zero(IntPtr p, int bytes)
        {
            for (int i = 0; i < bytes; i += 8) Marshal.WriteInt64(p, i, 0);
        }

        private static void WriteVariant(IntPtr p, object value, System.Collections.Generic.List<IntPtr> bstrs)
        {
            Zero(p, VariantSize);
            switch (value)
            {
                case null:
                    Marshal.WriteInt16(p, 0, (short)VT_EMPTY);
                    break;
                case int i:
                    Marshal.WriteInt16(p, 0, (short)VT_I4);
                    Marshal.WriteInt32(p, 8, i);
                    break;
                case bool b:
                    Marshal.WriteInt16(p, 0, (short)VT_BOOL);
                    Marshal.WriteInt16(p, 8, b ? (short)-1 : (short)0);
                    break;
                case string s:
                    var bstr = Marshal.StringToBSTR(s);
                    bstrs.Add(bstr); // freed after the call (callee copies)
                    Marshal.WriteInt16(p, 0, (short)VT_BSTR);
                    Marshal.WriteIntPtr(p, 8, bstr);
                    break;
                case ComDispatch d:
                    Marshal.WriteInt16(p, 0, (short)VT_DISPATCH);
                    Marshal.WriteIntPtr(p, 8, d._ptr); // borrowed for the call's duration
                    break;
                default:
                    throw new ArgumentException("Unsupported COM argument type: " + value.GetType());
            }
        }

        /// <summary>Read AND CONSUME a result VARIANT: strings are freed, object pointers' single
        /// reference transfers into the returned ComDispatch, byte arrays are copied + destroyed.</summary>
        private static object ReadVariant(IntPtr p)
        {
            ushort vt = (ushort)Marshal.ReadInt16(p, 0);
            switch (vt)
            {
                case VT_EMPTY: return null;
                case VT_I2: return (int)Marshal.ReadInt16(p, 8);
                case VT_I4: return Marshal.ReadInt32(p, 8);
                case VT_BOOL: return Marshal.ReadInt16(p, 8) != 0;
                case VT_BSTR:
                {
                    var bstr = Marshal.ReadIntPtr(p, 8);
                    if (bstr == IntPtr.Zero) return null;
                    var s = Marshal.PtrToStringBSTR(bstr);
                    SysFreeString(bstr);
                    return s;
                }
                case VT_DISPATCH:
                case VT_UNKNOWN:
                {
                    var ptr = Marshal.ReadIntPtr(p, 8);
                    return ptr == IntPtr.Zero ? null : new ComDispatch(ptr);
                }
                case VT_ARRAY | VT_UI1:
                {
                    var sa = Marshal.ReadIntPtr(p, 8);
                    if (sa == IntPtr.Zero) return null;
                    SafeArrayGetLBound(sa, 1, out int lo);
                    SafeArrayGetUBound(sa, 1, out int hi);
                    int len = hi - lo + 1;
                    var bytes = new byte[Math.Max(0, len)];
                    if (len > 0 && SafeArrayAccessData(sa, out var data) == 0)
                    {
                        Marshal.Copy(data, bytes, 0, len);
                        SafeArrayUnaccessData(sa);
                    }
                    SafeArrayDestroy(sa);
                    return bytes;
                }
                default:
                    throw new InvalidOperationException("Unsupported VARIANT result type: " + vt);
            }
        }
    }
}
