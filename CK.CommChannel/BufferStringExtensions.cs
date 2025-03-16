using System;
using System.Buffers;
using System.Text;

namespace CK.CommChannel
{
    public static class BufferStringUtilities
    {
        /// <summary>
        /// Appends the content of a <see cref="ReadOnlySequence{T}"/> of bytes to a <see cref="StringBuilder"/>.
        /// Characters in the ASCII printable range (32-126) are appended directly as characters.
        /// Non-printable characters are appended in hexadecimal format enclosed in angle brackets.
        /// </summary>
        /// <param name="sb">
        /// The <see cref="StringBuilder"/> to which the processed content is appended.
        /// </param>
        /// <param name="sequence">
        /// The <see cref="ReadOnlySequence{T}"/> containing the bytes to process.
        /// </param>
        /// <returns>
        /// The <see cref="StringBuilder"/> instance with the appended content.
        /// </returns>
        public static StringBuilder AppendBufferSequence( this StringBuilder sb, ReadOnlySequence<byte> sequence )
        {
            foreach( var memory in sequence )
            {
                AppendBufferSpan( sb, memory.Span );
            }

            return sb;
        }

        /// <summary>
        /// Appends the content of a <see cref="ReadOnlySpan{T}"/> of bytes to a <see cref="StringBuilder"/>.
        /// Characters in the ASCII printable range (32-126) are appended directly as characters.
        /// Non-printable characters are appended in hexadecimal format enclosed in angle brackets.
        /// </summary>
        /// <param name="sb">
        /// The <see cref="StringBuilder"/> to which the processed content is appended.
        /// </param>
        /// <param name="span">
        /// The <see cref="ReadOnlySpan{T}"/> containing the bytes to process.
        /// </param>
        /// <returns>
        /// The <see cref="StringBuilder"/> instance with the appended content.
        /// </returns>
        public static StringBuilder AppendBufferSpan( this StringBuilder sb, ReadOnlySpan<byte> span )
        {
            foreach( var b in span )
            {
                AppendBufferByte( sb, b );
            }

            return sb;
        }

        /// <summary>
        /// Appends a single byte to a <see cref="StringBuilder"/> as a character if it is within the ASCII printable range (32-126),
        /// or as a hexadecimal representation enclosed in angle brackets for non-printable bytes.
        /// </summary>
        /// <param name="sb">
        /// The <see cref="StringBuilder"/> to which the byte is appended.
        /// </param>
        /// <param name="b">
        /// The byte to append to the <see cref="StringBuilder"/>.
        /// </param>
        /// <returns>
        /// The <see cref="StringBuilder"/> instance with the appended content.
        /// </returns>
        public static StringBuilder AppendBufferByte( this StringBuilder sb, byte b )
        {
            if( b is >= 32 and <= 126 ) // ASCII printable range
            {
                sb.Append( (char) b );
            }
            else
            {
                sb.Append( $"<{b:X2}>" );
            }

            return sb;
        }
    }
}
