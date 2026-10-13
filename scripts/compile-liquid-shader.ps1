param(
    [string]$Source = (Join-Path $PSScriptRoot '../assets/shaders/liquid-refraction.hlsl'),
    [string]$Output = (Join-Path $PSScriptRoot '../assets/shaders/liquid-refraction.ps')
)

$ErrorActionPreference = 'Stop'

# d3dcompiler_47 ships with Windows. It is needed only when rebuilding this asset,
# never while running GitCompass. The checked-in .ps is a WPF ps_2_0 shader.
if (-not ('GitCompassShaderCompiler' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

public static class GitCompassShaderCompiler
{
    [DllImport("d3dcompiler_47.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern int D3DCompile(byte[] data, UIntPtr length,
        [MarshalAs(UnmanagedType.LPStr)] string sourceName, IntPtr defines, IntPtr include,
        [MarshalAs(UnmanagedType.LPStr)] string entryPoint,
        [MarshalAs(UnmanagedType.LPStr)] string target, uint flags1, uint flags2,
        out IntPtr code, out IntPtr errors);

    [DllImport("d3dcompiler_47.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern int D3DDisassemble(byte[] data, UIntPtr length, uint flags,
        [MarshalAs(UnmanagedType.LPStr)] string comments, out IntPtr disassembly);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate IntPtr GetBufferPointer(IntPtr instance);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate UIntPtr GetBufferSize(IntPtr instance);

    private static byte[] ReadBlob(IntPtr blob)
    {
        if (blob == IntPtr.Zero) return new byte[0];
        IntPtr vtable = Marshal.ReadIntPtr(blob);
        var pointer = (GetBufferPointer)Marshal.GetDelegateForFunctionPointer(
            Marshal.ReadIntPtr(vtable, 3 * IntPtr.Size), typeof(GetBufferPointer));
        var size = (GetBufferSize)Marshal.GetDelegateForFunctionPointer(
            Marshal.ReadIntPtr(vtable, 4 * IntPtr.Size), typeof(GetBufferSize));
        byte[] bytes = new byte[checked((int)size(blob).ToUInt64())];
        Marshal.Copy(pointer(blob), bytes, 0, bytes.Length);
        return bytes;
    }

    public static string Compile(string sourcePath, string outputPath)
    {
        byte[] source = File.ReadAllBytes(sourcePath);
        IntPtr code = IntPtr.Zero, errors = IntPtr.Zero, assembly = IntPtr.Zero;
        try
        {
            // Optimization level 3 and warnings as errors. The ps_2_0 profile
            // intentionally uses the legacy sampler2D syntax required by WPF.
            int result = D3DCompile(source, (UIntPtr)source.Length, sourcePath,
                IntPtr.Zero, IntPtr.Zero, "main", "ps_2_0", (1u << 15) | (1u << 18),
                0, out code, out errors);
            string diagnostics = Encoding.UTF8.GetString(ReadBlob(errors)).TrimEnd('\0');
            if (result < 0)
                throw new InvalidOperationException("HLSL compilation failed: " + diagnostics);
            byte[] bytecode = ReadBlob(code);
            File.WriteAllBytes(outputPath, bytecode);
            result = D3DDisassemble(bytecode, (UIntPtr)bytecode.Length, 0, null, out assembly);
            if (result < 0)
                throw new InvalidOperationException("Shader compiled, but disassembly failed.");
            return Encoding.UTF8.GetString(ReadBlob(assembly)).TrimEnd('\0');
        }
        finally
        {
            if (assembly != IntPtr.Zero) Marshal.Release(assembly);
            if (errors != IntPtr.Zero) Marshal.Release(errors);
            if (code != IntPtr.Zero) Marshal.Release(code);
        }
    }
}
'@
}

$sourcePath = (Resolve-Path -LiteralPath $Source).Path
$outputPath = [IO.Path]::GetFullPath($Output)
$disassembly = [GitCompassShaderCompiler]::Compile($sourcePath, $outputPath)
$instructionLine = ($disassembly -split "`n" | Where-Object { $_ -match 'instruction slots' }) -join ' '
Write-Output "Compiled main / ps_2_0: $outputPath"
Write-Output "Bytes: $((Get-Item -LiteralPath $outputPath).Length)"
Write-Output $instructionLine
