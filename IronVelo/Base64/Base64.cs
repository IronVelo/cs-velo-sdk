using System.Runtime.InteropServices;

namespace IronVelo.Base64;

using System.Diagnostics;
using System.Runtime.CompilerServices;
using Exceptions;

/*
 * ---- CT BASE64 ENCODE ----
   Mean of Left: 1174.898909090909
   Variance of Left: 285688485.643176
   Standard Deviation of Left: 16902.321900945328
   Mean of Right: 1174.7132424242425
   Variance of Right: 253267948.2235978
   Standard Deviation of Right: 15914.394371875978
   Welch's t-test statistic: 0.007957464455431721
   Degrees of freedom: 1972859.1349072268
   
   ---- BUILTIN BASE64 ENCODE ----
   Mean of Left: 262.15727272727275
   Variance of Left: 1353399.74957821
   Standard Deviation of Left: 1163.3571032052928
   Mean of Right: 274.5816
   Variance of Right: 82661435.35143079
   Standard Deviation of Right: 9091.833442789786
   Welch's t-test statistic: -1.348691096197641
   Degrees of freedom: 1022408.4376871253
   
   ---- CT BASE64 DECODE ----
   Mean of Left: 2112.3739393939395
   Variance of Left: 40490221.2954933
   Standard Deviation of Left: 6363.192696712343
   Mean of Right: 2122.010325252525
   Variance of Right: 1206296.3030557402
   Standard Deviation of Right: 1098.3152111555864
   Welch's t-test statistic: -1.4848476170648048
   Degrees of freedom: 1048935.358386001
   
   ---- BUILTIN BASE64 DECODE ----
   Mean of Left: 708.9080121212121
   Variance of Left: 851175.3413429648
   Standard Deviation of Left: 922.5916438722847
   Mean of Right: 752.6765535353535
   Variance of Right: 17354548.72761547
   Standard Deviation of Right: 4165.879106217014
   Welch's t-test statistic: -10.206473051389832
   Degrees of freedom: 1086877.4104864302
 */

/// <summary>
/// The first constant-time Base64 implementation in C#.
/// </summary>
/// <remarks>
/// This class provides methods for encoding and decoding Base64 strings in a manner that prevents timing attacks.
/// It includes both constant-time implementations and a high-performance non-constant-time implementation.
/// All methods operate without padding.
/// <br/>
/// <para>
/// <b>Background:</b>
/// In security-sensitive applications, it's crucial to avoid timing attacks that can leak information about the
/// processed data. 
/// This implementation ensures that the encoding and decoding of Base64 strings take constant time, regardless of the
/// input data's contents, thus mitigating the risk of such attacks.
/// </para>
/// <para>
/// <b>Methods:</b>
/// <list type="bullet"><item>
/// <term><see cref="EncodeCt(ReadOnlySpan{byte})"/></term>
/// <description>
/// Encodes data to Base64 without padding, designed to prevent side-channel attacks. This method is slower but ensures
/// constant-time operations.
/// </description>
/// </item><item>
/// <term><see cref="DecodeCt(string)"/></term>
/// <description>
/// Decodes Base64 data without padding, designed to prevent side-channel attacks. This method is slower but ensures
/// constant-time operations.
/// </description>
/// </item><item>
/// <term><see cref="Decode(string)"/></term>
/// <description>
/// Decodes Base64 data without padding. This method is optimized for performance and is faster than both the
/// constant-time and builtin versions, but does not guarantee constant-time behavior.
/// </description>
/// </item></list>
/// </para>
/// </remarks>
public static class Base64
{
    private static uint DecodedLength(uint encodedLength)
    {
        return encodedLength * 3 / 4;
    }

    private static uint EncodedLength(uint decodedLen)
    {
        return (decodedLen * 4 + 2) / 3;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static short Decode6Bits(char src)
    {
        var ch = (short)src;
        short ret = -1;

        ret += (short)((((0x40 - ch) & (ch - 0x5b)) >> 8) & (ch - 64));
        ret += (short)((((0x60 - ch) & (ch - 0x7b)) >> 8) & (ch - 70));
        ret += (short)((((0x2f - ch) & (ch - 0x3a)) >> 8) & (ch + 5));
        ret += (short)((((0x2a - ch) & (ch - 0x2c)) >> 8) & 63);
        ret += (short)((((0x2e - ch) & (ch - 0x30)) >> 8) & 64);

        return ret;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe short Decode3Bytes(byte* dest, char* src)
    {
        var c0 = Decode6Bits(src[0]);
        var c1 = Decode6Bits(src[1]);
        var c2 = Decode6Bits(src[2]);
        var c3 = Decode6Bits(src[3]);
        
        dest[0] = (byte)((c0 << 2) | ((c1 >> 4) & 0x3F));
        dest[1] = (byte)((c1 << 4) | ((c2 >> 2) & 0xF));
        dest[2] = (byte)((c2 << 6) | (c3 & 0x3F));
        
        return (short)(((c0 | c1 | (ushort)c2 | (ushort)c3) >> 8) & 1);
    }
    
    private static unsafe void DecodeIntoCt(char* src, char* endSrc, byte* dest)
    {
        Debug.Assert(src != null && dest != null, "Arguments for decoding cannot be null");
        
        var valid = 0;

        while (src + 4 <= endSrc)
        {
            valid |= (ushort)Decode3Bytes(dest, src);
            src += 4;
            dest += 3;
        }
        
        switch (endSrc - src)
        {
            case 2:
            {
                var c0 = Decode6Bits(*src++);
                var c1 = Decode6Bits(*src);
                
                valid |= ((c0 | c1) >> 8) & 1;

                *dest = (byte)((c0 << 2) | (c1 >> 4));
                break;
            }
            case 3:
            {
                var c0 = Decode6Bits(*src++);
                var c1 = Decode6Bits(*src++);
                var c2 = Decode6Bits(*src);
                
                valid |= ((c0 | c1 | (ushort)c2) >> 8) & 1;

                *dest++ = (byte)((c0 << 2) | (c1 >> 4));
                *dest = (byte)((c1 << 4) | (c2 >> 2));
                break;
            }
        }
        
        if (valid != 0)
        {
            throw Base64Error.InvalidEncoding;
        }
    }

    private static ReadOnlySpan<char> CharSpan(string src)
    {
        unsafe
        {
            fixed (char* sPtr = src)
            {
                return new ReadOnlySpan<char>(sPtr, src.Length);
            }
        }
    }
    
    /// <summary>
    /// Decodes Base64 data without padding, designed to prevent side-channel attacks.
    ///
    /// This method is significantly slower than <see cref="Decode"/>, but avoids any branching or use of
    /// lookup tables on the data. Timing variations based on data contents can leak information, allowing for
    /// side-channel attacks. This method is designed to mitigate such risks.
    /// </summary>
    /// <param name="base64">The Base64 encoded data which may or may not be sensitive.</param>
    /// <returns>The raw decoded byte array.</returns>
    /// <exception cref="Base64Error.InvalidEncoding">If the data being decoded was not valid base64.</exception>
    /// <remarks>
    /// <para>
    /// <b>Testing and Verification:</b>
    /// Various methodologies were employed to test both the correctness and constant-time properties of this
    /// implementation.
    /// <list type="bullet"><item>
    /// <description>
    /// <b>Statistical Timing Analysis:</b> Performed statistical analysis of timing properties using methodologies from
    /// the paper <c>Dude, is my code constant time?</c> <see href="https://eprint.iacr.org/2016/1123.pdf"/>, with
    /// sample collection emulating methodologies used in cache timing attacks.
    /// </description>
    /// </item><item>
    /// <description>
    /// <b>Property Testing with FsCheck:</b> Ensured bijectivity of the implementation with both its encode interface and
    /// the built-in (with padding removed).
    /// </description>
    /// </item></list>
    /// </para>
    /// <para>
    /// <b>Performance:</b>
    /// This method was designed for security, not performance. While the performance is acceptable, it is slower than
    /// the built-in implementation. If security is a concern, use this method regardless of performance considerations,
    /// or consider using an FFI to another constant-time implementation from a different language.
    /// <br/>
    /// For more information regarding the performance relative to this method, the built-in implementation, and
    /// <see cref="Decode"/>, see the <c>Benchmarks</c> project.
    /// </para>
    /// <para>
    /// <b>Complexities of Constant-Time Programming:</b>
    /// Achieving constant-time execution is extremely complex and depends on various factors including the hardware,
    /// the operating system, and the version of .NET being used. While our testing indicates that this implementation
    /// achieves constant-time behavior, it is important to understand that constant-time programming is a nuanced and
    /// intricate field. Variations in environment can affect the constant-time properties of this method.
    /// </para>
    /// </remarks>
    public static byte[] DecodeCt(string base64)
    {
        var outputLength = (int)DecodedLength((uint)base64.Length);
        var output = new byte[outputLength];
        
        unsafe
        {
            /* Why operate on raw pointers?
             *
             * One might assume that this choice was made for performance reasons. In reality, it was to ensure
             * constant-time properties. Our statistical analysis indicated that the JIT compiler is less likely to
             * optimize out constant-time methodologies when using raw pointers. This approach reduced the
             * Welch's T-Test score by a factor of 2.
             */
            fixed (char* srcPtr = &MemoryMarshal.GetReference<char>(base64))
            fixed (byte* destPtr = &MemoryMarshal.GetReference<byte>(output))
            {
                var endSrcPtr = srcPtr + base64.Length;
                DecodeIntoCt(srcPtr, endSrcPtr, destPtr);
            }
            
            return output;
        }
    }
    
    private static readonly byte[] Base64Inv = {
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, // 0-15
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, // 16-31
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 62, 255, 255, 255, 63, // 32-47 ('+' = 62, '/' = 63)
        52, 53, 54, 55, 56, 57, 58, 59, 60, 61, 255, 255, 255, 255, 255, 255, // 48-63 ('0'-'9' = 52-61)
        255, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, // 64-79 ('A'-'O')
        15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 255, 255, 255, 255, 255, // 80-95 ('P'-'Z')
        255, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, // 96-111 ('a'-'o')
        41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 51, 255, 255, 255, 255, 255, // 112-127 ('p'-'z')
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, // padding, invalid
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255
    };
    
    private static void DecodeInto(ReadOnlySpan<char> src, Span<byte> dest)
    {
        var length = src.Length;
        var idx = 0;
        var inIdx = 0;
        var invalid = (byte)0;

        while (inIdx + 4 <= length)
        {
            var c1 = Base64Inv[src[inIdx]];
            var c2 = Base64Inv[src[1 + inIdx]];
            var c3 = Base64Inv[src[2 + inIdx]];
            var c4 = Base64Inv[src[3 + inIdx]];
            
            invalid |= (byte)(
                (c1 & 0x80) | (c2 & 0x80) | (c3 & 0x80) | (c4 & 0x80)
            );
            
            var bits = ((uint)c1 << 18) 
                       | ((uint)c2 << 12) 
                       | ((uint)c3 << 6)
                       | c4;
            
            dest[idx] = (byte)(bits >> 16);
            dest[idx + 1] = (byte)(bits >> 8);
            dest[idx + 2] = (byte)bits;

            inIdx += 4;
            idx += 3;
        }

        switch (length - inIdx)
        {
            case 2:
                var c11 = Base64Inv[src[0 + inIdx]];
                var c12 = Base64Inv[src[1 + inIdx]];
                
                invalid |= (byte)((c11 & 0x80) | (c12 & 0x80));
                
                var bits = ((uint)c11 << 18) | ((uint)c12 << 12);
                dest[idx] = (byte)(bits >> 16);
                break;
            case 3:
                var c21 = Base64Inv[src[0 + inIdx]];
                var c22 = Base64Inv[src[1 + inIdx]];
                var c23 = Base64Inv[src[2 + inIdx]];
                
                invalid |= (byte)((c21 & 0x80) | (c22 & 0x80) | (c23 & 0x80));
                
                var obits = ((uint)c21 << 18)
                           | ((uint)c22 << 12)
                           | ((uint)c23 << 6);
                
                dest[idx] = (byte)(obits >> 16);
                dest[idx + 1] = (byte)(obits >> 8);
                break;
        }
        if (invalid != 0)
        {
            throw Base64Error.InvalidEncoding;
        }
    }
    
    /// <summary>
    /// Decodes Base64 data without padding.
    /// 
    /// This method is optimized for performance and outperforms the built-in Base64 decoding implementation.
    /// </summary>
    /// <param name="base64">The Base64 encoded data which is not sensitive.</param>
    /// <returns>The raw decoded byte array.</returns>
    /// <exception cref="Base64Error.InvalidEncoding">If the data being decoded was not valid base64.</exception>
    /// <remarks>
    /// <para>
    /// <b>Performance:</b>
    /// This method is designed to be highly performant and efficient. In benchmarks, it outperforms the built-in Base64
    /// decoding implementation. This makes it suitable for use cases where decoding speed is critical.
    /// </para>
    /// <br/>
    /// <b>Security:</b>
    /// If you're decoding sensitive information this can potentially leak information about the contents, to avoid this
    /// behavior see <see cref="Base64.DecodeCt"/>.
    /// </remarks> 
    public static byte[] Decode(string base64)
    {
        var outputLength = (int)DecodedLength((uint)base64.Length);
        var output = new byte[outputLength];
        
        DecodeInto(CharSpan(base64), output);
        
        return output;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte Encode6Bits(short src)
    {
        var diff = src + 0x41;
        
        diff += ((25 - src) >> 8) & 6;
        diff -= ((51 - src) >> 8) & 75;
        diff -= ((61 - src) >> 8) & 15;
        diff += ((62 - src) >> 8) & 3;
        
        return (byte)diff;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void Encode3Bytes(char* dest, byte* src)
    {
        var b0 = (short)src[0];
        var b1 = (short)src[1];
        var b2 = (short)src[2];

        dest[0] = (char)Encode6Bits((short)(b0 >> 2));
        dest[1] = (char)Encode6Bits((short)(((b0 << 4) | (b1 >> 4)) & 63));
        dest[2] = (char)Encode6Bits((short)(((b1 << 2) | (b2 >> 6)) & 63));
        dest[3] = (char)Encode6Bits((short)(b2 & 63));
    }

    private static unsafe int EncodeIntoCt(byte* src, byte* endSrc, char* dest, int destLen)
    {
        Debug.Assert(destLen >= EncodedLength((uint)(endSrc - src)));
        
        while (src + 3 <= endSrc)
        {
            Encode3Bytes(dest, src);
            src += 3;
            dest += 4;
        }
        
        Debug.Assert(endSrc - src <= 2);
        
        switch (endSrc - src)
        {
            case 1:
                var b10 = (short)*src;
                *dest++ = (char)Encode6Bits((short)(b10 >> 2));
                *dest   = (char)Encode6Bits((short)((b10 << 4) & 63));
                return 2;
            case 2:
                var b20 = (short)*src;
                var b21 = (short)*(src + 1);
                *dest++ = (char)Encode6Bits((short)(b20 >> 2));
                *dest++ = (char)Encode6Bits((short)(((b20 << 4) | (b21 >> 4)) & 63));
                *dest   = (char)Encode6Bits((short)((b21 << 2) & 63));
                return 1;
        }
        
        return 0;
    }
    
    /// <summary>
    /// Encodes data to Base64 without padding, designed to prevent side-channel attacks.
    /// 
    /// This method is significantly slower than standard Base64 encoding methods, but avoids any branching or use of
    /// lookup tables on the data. Timing variations based on data contents can leak information, allowing for
    /// side-channel attacks. This method is designed to mitigate such risks.
    ///
    /// </summary>
    /// <param name="src">The raw data to be encoded.</param>
    /// <returns>The Base64 encoded string without padding.</returns>
    /// <remarks>
    /// <para>
    /// <b>Testing and Verification:</b>
    /// Various methodologies were employed to test both the correctness and constant-time properties of this
    /// implementation.
    /// <list type="bullet"><item>
    /// <description>
    /// <b>Statistical Timing Analysis:</b> Performed statistical analysis of timing properties using methodologies from
    /// the paper <see href="https://eprint.iacr.org/2016/1123.pdf">Dude, is my code constant time?</see>, with
    /// sample collection emulating methodologies used in cache timing attacks.
    /// </description>
    /// </item><item>
    /// <description>
    /// <b>Property Testing with FsCheck:</b> Ensured bijectivity of the implementation with both its decode interface
    /// and the built-in Base64 decode (with padding appended).
    /// </description>
    /// </item></list>
    /// </para>
    /// <para>
    /// <b>Performance:</b>
    /// This method was designed for security, not performance. While the performance is acceptable, it is slower than
    /// the built-in implementation. If security is a concern, use this method regardless of performance considerations,
    /// or consider using an FFI to another constant-time implementation from a different language.
    /// <br/>
    /// For more information regarding the performance relative to this method, the built-in implementation, and
    /// other variations, see the <c>Benchmarks</c> project.
    /// </para>
    /// <para>
    /// <b>Complexities of Constant-Time Programming:</b>
    /// Achieving constant-time execution is extremely complex and depends on various factors including the hardware,
    /// the operating system, and the version of .NET being used. While our testing indicates that this implementation
    /// achieves constant-time behavior, it is important to understand that constant-time programming is a nuanced and
    /// intricate field. Variations in environment can affect the constant-time properties of this method.
    /// </para>
    /// </remarks> 
    public static string EncodeCt(ReadOnlySpan<byte> src)
    {
        var encodedLength = (int)EncodedLength((uint)src.Length);

        unsafe
        {
            var result = new string('\0', encodedLength);

            /* For an explanation of why we're operating on raw pointers see the `DecodeCt` implementation */
            fixed (char* destPtr = &MemoryMarshal.GetReference<char>(result))
            fixed (byte* srcPtr = &MemoryMarshal.GetReference(src))
            {
                var endSrcPtr = srcPtr + src.Length;
                EncodeIntoCt(srcPtr, endSrcPtr, destPtr, encodedLength);
            }

            return result;
        }
    }

    /// <summary>
    /// Takes the encoded length without padding and computes the final length with padding
    /// </summary>
    /// <param name="encodedNoPadLength">The encoded length without padding</param>
    /// <returns>The encoded length with padding</returns>
    private static uint PadEncodedLength(uint encodedNoPadLength)
    {
        return (uint)((encodedNoPadLength + 3) & ~3);
    }
    
    /// <summary>
    /// Encodes data to Base64 with padding, designed to prevent side-channel attacks.
    /// 
    /// This method is significantly slower than standard Base64 encoding methods, but avoids any branching or use of
    /// lookup tables on the data. Timing variations based on data contents can leak information, allowing for
    /// side-channel attacks. This method is designed to mitigate such risks.
    ///
    /// </summary>
    /// <param name="src">The raw data to be encoded.</param>
    /// <returns>The Base64 encoded string with padding.</returns>
    /// <remarks>
    /// <para>
    /// <b>Testing and Verification:</b>
    /// Various methodologies were employed to test both the correctness and constant-time properties of this
    /// implementation.
    /// <list type="bullet"><item>
    /// <description>
    /// <b>Statistical Timing Analysis:</b> Performed statistical analysis of timing properties using methodologies from
    /// the paper <see href="https://eprint.iacr.org/2016/1123.pdf">Dude, is my code constant time?</see>, with
    /// sample collection emulating methodologies used in cache timing attacks.
    /// </description>
    /// </item><item>
    /// <description>
    /// <b>Property Testing with FsCheck:</b> Ensured bijectivity of the implementation with both its decode interface
    /// and the built-in Base64 decode (with padding appended).
    /// </description>
    /// </item></list>
    /// </para>
    /// <para>
    /// <b>Performance:</b>
    /// This method was designed for security, not performance. While the performance is acceptable, it is slower than
    /// the built-in implementation. If security is a concern, use this method regardless of performance considerations,
    /// or consider using an FFI to another constant-time implementation from a different language.
    /// <br/>
    /// For more information regarding the performance relative to this method, the built-in implementation, and
    /// other variations, see the <c>Benchmarks</c> project.
    /// </para>
    /// <para>
    /// <b>Complexities of Constant-Time Programming:</b>
    /// Achieving constant-time execution is extremely complex and depends on various factors including the hardware,
    /// the operating system, and the version of .NET being used. While our testing indicates that this implementation
    /// achieves constant-time behavior, it is important to understand that constant-time programming is a nuanced and
    /// intricate field. Variations in environment can affect the constant-time properties of this method.
    /// </para>
    /// </remarks> 
    public static string EncodePaddedCt(ReadOnlySpan<byte> src)
    {
        var encodedNoPadLength = EncodedLength((uint)src.Length);
        var encodedLength = (int)PadEncodedLength(encodedNoPadLength);

        unsafe
        {
            var result = new string('\0', encodedLength);
            int toPad;
            
            /* For an explanation of why we're operating on raw pointers see the `DecodeCt` implementation */
            fixed (char* destPtr = &MemoryMarshal.GetReference<char>(result))
            fixed (byte* srcPtr = &MemoryMarshal.GetReference(src))
            {
                var endSrcPtr = srcPtr + src.Length;
                toPad = EncodeIntoCt(srcPtr, endSrcPtr, destPtr, encodedLength);
            }
            
            {
                fixed (char* resultPtr = &MemoryMarshal.GetReference<char>(result))
                {
                    var paddingPtr = resultPtr + encodedNoPadLength;
                    for (var i = 0; i < toPad; i++)
                    {
                        *(paddingPtr + i) = '=';
                    }
                }
            }
            
            return result;
        }
    }
}