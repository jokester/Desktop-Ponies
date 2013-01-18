﻿namespace CsDesktopPonies.SpriteManagement
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Drawing.Imaging;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Runtime.InteropServices;

    #region BufferToImage delegate
    /// <summary>
    /// Takes the raw decoded buffer, and turns them it into an image of type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The type of the image to create.</typeparam>
    /// <param name="buffer">The values that make up the image. The buffer is of logical size stride * height. It is of physical size
    /// stride * height * depth / 8.</param>
    /// <param name="palette">The color palette for the image. Each value in the buffer is an index into the array. Note the size of the
    /// palette can exceed the maximum size addressable by values in the buffer.</param>
    /// <param name="transparentIndex">The index of the transparent color, or -1 to indicate no transparency. Where possible, this index in
    /// the palette should be replaced by transparency in the resulting frame, or else some suitable replacement. The color value in the
    /// palette for this index is undefined.</param>
    /// <param name="stride">The stride width, in bytes, of the buffer.</param>
    /// <param name="width">The logical width of the image the buffer contains.</param>
    /// <param name="height">The logical height of the image the buffer contains.</param>
    /// <param name="depth">The bit depth of the buffer (either 1, 2, 4 or 8).</param>
    /// <param name="hashCode">A pre-calculated hash code of the frame.</param>
    /// <returns>A new <typeparamref name="T"/> image, constructed from the given raw buffer and palette.</returns>
    /// <remarks>
    /// <para>The <see cref="T:CsDesktopPonies.SpriteManagement.GifImage`1"/> will decode an image into a low-level buffer and palette.
    /// This function allows you to construct a graphics object of your choosing from this buffer. A typical example would be a
    /// <see cref="T:System.Drawing.Bitmap"/>.</para>
    /// <para>Usually, you will copy the buffer row by row to the target buffer of your object, in order to account for differing stride
    /// widths. Example pseudocode is provided for a theoretical Image object.</para>
    /// <example>
    /// <code><![CDATA[
    /// // Create an image of the given width and height in pixels, and of the given bit depth.
    /// // The GifDecoder can be constrained to produce certain bit depths, for formats that only support certain values.
    /// Image frame = new Image(width, height, depth);
    /// 
    /// // Assume the image provides access to its buffer. Different formats allow different things. Some may allow an array to be passed
    /// // in as a parameter in the constructor, and then lock it.  Others may require use of marshaling to get access.
    /// byte[] frameBuffer = frame.Buffer;
    /// 
    /// // Copy over row by row.
    /// for(int row = 0; row < height; row++)
    /// {
    ///     // Copy from the source buffer to the destination buffer.
    ///     // Notice how the starting index for each array is dependant of the stride width of their respective sources.
    ///     Array.Copy(buffer, row * stride, frameBuffer, row * frame.Stride, stride);
    /// }
    /// 
    /// // Copy over the source palette, into a palette made up of theoretical Color elements.
    /// // Note we cannot be sure which palette will be bigger, and thus must check the bounds of each.
    /// for(int i = 0; i < palette.Length && i < frame.Palette.Length; i++)
    ///     frame.Palette[i] = new Color(palette[i].R, palette[i].G, palette[i].B);
    /// 
    /// // Apply the transparent color. We assume our theoretical color has an alpha channel.
    /// if(transparentIndex != -1)
    ///     frame.Palette[transparentIndex] = Color.Transparent;
    ///     
    /// // Should you require it, a pre-calculated hash code for the image is provided.
    /// 
    /// // The image is complete.
    /// return frame;
    /// ]]></code></example>
    /// </remarks>
    public delegate T BufferToImage<T>(
    byte[] buffer, RgbColor[] palette, int transparentIndex, int stride, int width, int height, int depth, int hashCode);
    #endregion

    /// <summary>
    /// Provides implementations of <see cref="T:CsDesktopPonies.SpriteManagement.GifImage`1"/> using various formats.
    /// </summary>
    public static class GifImage
    {
        #region bitmapImageFactory function
        /// <summary>
        /// Represents the method that converts a buffer into an <see cref="T:System.Drawing.Bitmap"/>.
        /// </summary>
        private static BufferToImage<Bitmap> bitmapImageFactory = 
            (byte[] buffer, RgbColor[] palette, int transparentIndex, int stride, int width, int height, int depth, int hashCode) =>
            {
                PixelFormat targetFormat;
                if (depth == 1)
                    targetFormat = PixelFormat.Format1bppIndexed;
                else if (depth == 4)
                    targetFormat = PixelFormat.Format4bppIndexed;
                else if (depth == 8)
                    targetFormat = PixelFormat.Format8bppIndexed;
                else
                    throw new ArgumentOutOfRangeException("depth", depth, "depth must be 1, 4 or 8.");

                // Create the bitmap and lock it.
                Bitmap bitmap = new Bitmap(width, height, targetFormat);
                BitmapData data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, bitmap.PixelFormat);

                // Copy the frame buffer to the bitmap. To account for stride padding, copy row by row. Then unlock it.
                for (int row = 0; row < data.Height; row++)
                    Marshal.Copy(
                        buffer,
                        row * stride,
                        IntPtr.Add(data.Scan0, row * data.Stride),
                        stride);
                bitmap.UnlockBits(data);

                // Fill in the color palette from the current table.
                ColorPalette bitmapPalette = bitmap.Palette;
                for (int i = 0; i < palette.Length; i++)
                    bitmapPalette.Entries[i] = Color.FromArgb(palette[i].ToArgb());

                // Apply transparency.
                if (transparentIndex != -1)
                    bitmapPalette.Entries[transparentIndex] = Color.Transparent;

                // Set palette on bitmap.
                bitmap.Palette = bitmapPalette;

                return bitmap;
            };
        #endregion

        /// <summary>
        /// Initializes a new instance of the <see cref="T:CsDesktopPonies.SpriteManagement.GifImage`1"/> class of type
        /// <see cref="T:System.Drawing.Bitmap"/> by decoding a GIF from the given stream.
        /// </summary>
        /// <param name="stream">A <see cref="T:System.IO.Stream"/> ready to read a GIF file.</param>
        /// <returns>A new <see cref="T:CsDesktopPonies.SpriteManagement.GifImage`1"/> of type <see cref="T:System.Drawing.Bitmap"/>
        /// initialized from the given stream.</returns>
        /// <exception cref="T:System.ArgumentNullException"><paramref name="stream"/> is null.</exception>
        /// <exception cref="T:System.ArgumentException"><paramref name="stream"/> does not support reading.</exception>
        /// <exception cref="T:System.NotSupportedException">The file uses a feature of GIF that is not supported by the decoder.
        /// </exception>
        /// <exception cref="T:System.IO.InvalidDataException"><paramref name="stream"/> was not a GIF file, or was a badly formed GIF
        /// file.</exception>
        public static GifImage<Bitmap> OfBitmap(Stream stream)
        {
            return new GifImage<Bitmap>(stream, bitmapImageFactory, BitDepths.Indexed1Bpp | BitDepths.Indexed4Bpp | BitDepths.Indexed8Bpp);
        }
    }

    /// <summary>
    /// Decodes a GIF file into its component frames.
    /// </summary>
    /// <typeparam name="T">The type of the frame images.</typeparam>
    /// <remarks>
    /// This class provides easy access to the frames of an animated GIF file, as well as allowing the type of the image used for each
    /// frame to be specified so different graphics formats can make use of the class.
    /// </remarks>
    public sealed class GifImage<T>
    {
        #region BlockCode enum
        /// <summary>
        /// Defines block codes used to identify the type of block.
        /// </summary>
        private enum BlockCode : byte
        {
            /// <summary>
            /// Specifies the block is an image descriptor block (Value 0x2C, 44).
            /// </summary>
            ImageDescriptor = 44,
            /// <summary>
            /// Specifies the block is an extension block (Value 0x21, 33).
            /// </summary>
            Extension = 33,
            /// <summary>
            /// Specifies the block is the data stream trailer, and that the stream has ended (Value 0x3B, 59).
            /// </summary>
            Trailer = 59
        }
        #endregion

        #region ExtensionLabel enum
        /// <summary>
        /// Defines extension labels used within extension blocks to identify the type of extension.
        /// </summary>
        private enum ExtensionLabel : byte
        {
            /// <summary>
            /// Specifies the extension is a graphic control extension (Value 0xF9, 249).
            /// </summary>
            GraphicControl = 0xF9,
            /// <summary>
            /// Specifies the extension is an application extension (Value 0xFF, 255).
            /// </summary>
            Application = 0xFF,
            /// <summary>
            /// Specifies the extension is a comment extension (Value 0xFE, 254).
            /// </summary>
            Comment = 0xFE,
            /// <summary>
            /// Specifies the extension is a plain text extension (Value 0x01, 1).
            /// </summary>
            PlainText = 0x01,
        }
        #endregion

        #region DataBuffer class
        /// <summary>
        /// Provides a backing store for a set of values in 2-dimensions, with a given number of bits used for each value.
        /// </summary>
        private class DataBuffer
        {
            /// <summary>
            /// The number of bits used per value in the buffer.
            /// </summary>
            private byte bitsPerValue;
            /// <summary>
            /// Gets the number of bits used per value in the buffer.
            /// </summary>
            public byte BitsPerValue
            {
                get { return bitsPerValue; }
            }
            /// <summary>
            /// Gets the number of values that are stored in each byte of the buffer.
            /// </summary>
            public byte ValuesPerByte
            {
                get { return (byte)(8 / bitsPerValue); }
            }
            /// <summary>
            /// Gets the maximum value that may be stored given the current BitsPerValue of the buffer.
            /// </summary>
            public byte MaxValue
            {
                get { return (byte)((1 << bitsPerValue) - 1); }
            }
            /// <summary>
            /// Gets the logical dimensions of the buffer.
            /// </summary>
            public Size Size { get; private set; }
            /// <summary>
            /// Gets the stride width of the buffer.
            /// </summary>
            public int Stride { get; private set; }
            /// <summary>
            /// Gets the underlying array of bytes for this buffer.
            /// </summary>
            public byte[] Buffer { get; private set; }

            /// <summary>
            /// Initializes a new instance of the <see cref="T:CsDesktopPonies.SpriteManagement.GifImage`1.DataBuffer"/> class capable of
            /// holding enough values for the specified dimensions, using the given number of bits to store each value.
            /// </summary>
            /// <param name="dimensions">The dimensions of the buffer to create.</param>
            /// <param name="bitsPerValue">The number of bits used to store each value. Any values given must be representable in this
            /// number of bits. The number of bits can only be 1, 2, 4 or 8.</param>
            /// <param name="initialValue">The initial value to use for each entry in the buffer.</param>
            /// <exception cref="T:System.ArgumentOutOfRangeException"><paramref name="bitsPerValue"/> is not 1, 2, 4 or 8.-or-
            /// <paramref name="initialValue"/> is greater than the largest value that can be stored in the given number of bits.
            /// </exception>
            public DataBuffer(Size dimensions, byte bitsPerValue, byte initialValue = 0)
            {
                if (bitsPerValue != 1 && bitsPerValue != 2 && bitsPerValue != 4 && bitsPerValue != 8)
                    throw new ArgumentOutOfRangeException("bitsPerValue", bitsPerValue, "bitsPerValue may only be 1, 2, 4 or 8.");

                this.bitsPerValue = bitsPerValue;

                if (initialValue > MaxValue)
                    throw new ArgumentOutOfRangeException("initialValue", initialValue,
                        "initialValue is greater than the largest value that can be stored in the given number of bits (" +
                        MaxValue + ").");

                Size = dimensions;
                Stride = (int)Math.Ceiling((Size.Width * BitsPerValue) / 8f);
                Buffer = new byte[Stride * Size.Height];

                if (initialValue != 0)
                    FillBuffer(initialValue);
            }
            /// <summary>
            /// Efficiently sets all values in the buffer to the given value.
            /// </summary>
            /// <param name="value">The value to set for each entry in the buffer.</param>
            public void FillBuffer(byte value)
            {
                byte byteValue = AllValuesInByte(value);
                for (int i = 0; i < Buffer.Length; i++)
                    Buffer[i] = byteValue;
            }
            /// <summary>
            /// Efficiently sets all values in the buffer within the given bounds to the given value.
            /// </summary>
            /// <param name="value">The value to set for each entry in the buffer.</param>
            /// <param name="bounds">The area in which to apply the value. Areas outside these bounds will not be changed.</param>
            /// <exception cref="T:System.ArgumentException"><paramref name="bounds"/> extends outside the area of the buffer.</exception>
            public void FillBuffer(byte value, Rectangle bounds)
            {
                byte byteValue = AllValuesInByte(value);
                FillBuffer(bounds, (x, y) => value, i => byteValue);
            }
            /// <summary>
            /// Efficiently sets all values in the buffer within the given bounds to the values contained in the source buffer.
            /// </summary>
            /// <param name="source">The buffer whose values are the source values to set.</param>
            /// <param name="bounds">The area in which to change values. Areas outside these bounds will not be changed.</param>
            /// <exception cref="T:System.ArgumentException"><paramref name="bounds"/> extends outside the area of the buffer.</exception>
            public void FillBuffer(DataBuffer source, Rectangle bounds)
            {
                FillBuffer(bounds, (x, y) => source.GetValue(x, y), i => source.Buffer[i]);
            }
            /// <summary>
            /// Efficiently sets all values in the buffer within the given bounds to the values created by the given functions.
            /// </summary>
            /// <param name="bounds">The area in which to apply the value. Areas outside these bounds will not be changed.</param>
            /// <param name="valueFactory">Function that returns the value to set, given an x and y location in the buffer.</param>
            /// <param name="bufferValueFactory">Function that returns a byte to set, given an index in the buffer.</param>
            /// <exception cref="T:System.ArgumentException"><paramref name="bounds"/> extends outside the area of the buffer.</exception>
            private void FillBuffer(Rectangle bounds, Func<int, int, byte> valueFactory, Func<int, byte> bufferValueFactory)
            {
                if (!new Rectangle(Point.Empty, Size).Contains(bounds))
                    throw new ArgumentException("Given bounds must not extend outside the area of the buffer.", "bounds");

                // We want to set all the indices within the given area only.

                // Easy method: iterate over the values within area and SetValue everything.
                // Repeated bit twiddling on the same bytes isn't the fastest though.
                // for (int y = bounds.Top; y < bounds.Bottom; y++)
                //     for (int x = bounds.Left; x < bounds.Right; x++)
                //         SetValue(x, y, value);

                // Iterate over each row.
                // Use SetValue on the left and right edges of the row, when bit twiddling is required.
                // We can then speed things up by using the contiguous middle region and setting it with our precomputed value.
                for (int y = bounds.Top; y < bounds.Bottom; y++)
                {
                    int x = bounds.Left;
                    int i = Seek(x, y);
                    // Set values until we are aligned on a byte
                    while (i % ValuesPerByte != 0 && x < bounds.Right)
                    {
                        SetValue(x, y, valueFactory(x, y));
                        x++;
                        i++;
                    }
                    // Set all the bytes we can on this row.
                    if (x < bounds.Right)
                    {
                        int rowAlignmentEnd = (bounds.Right + bounds.Width * y) / ValuesPerByte;
                        for (int j = i / ValuesPerByte; j < rowAlignmentEnd; j++)
                        {
                            Buffer[j] = bufferValueFactory(j);
                            x += ValuesPerByte;
                        }
                    }
                    // Set values in the unaligned area until the row is done.
                    while (x < bounds.Right)
                    {
                        SetValue(x, y, valueFactory(x, y));
                        x++;
                    }
                }
            }
            /// <summary>
            /// Gets a byte with all values in that byte set to the given value.
            /// </summary>
            /// <param name="value">The value to set for each entry in the byte.</param>
            /// <returns>A byte with all values in that byte set to the given value.</returns>
            private byte AllValuesInByte(byte value)
            {
                byte resultValue = value;
                if (BitsPerValue != 8 && value != byte.MinValue && value != byte.MaxValue)
                {
                    if (BitsPerValue == 4)
                        resultValue = (byte)((value << 4) | value);
                    else if (BitsPerValue == 2)
                        resultValue = (byte)((value << 6) | (value << 4) | (value << 2) | value);
                    else if (BitsPerValue == 1)
                        resultValue = (byte)((value << 7) | (value << 6) | (value << 5) | (value << 4) |
                            (value << 3) | (value << 2) | (value << 1) | value);
                }
                return resultValue;
            }
            /// <summary>
            /// Gets the logical index of the value to seek along the single dimension of the buffer.
            /// </summary>
            /// <param name="x">The x co-ordinate of the value.</param>
            /// <param name="y">The y co-ordinate of the value.</param>
            /// <returns>The logical index to seek, along the single dimension of the buffer.</returns>
            private int Seek(int x, int y)
            {
                return Stride * y * ValuesPerByte + x;
            }
            /// <summary>
            /// Gets the value from the given location in the buffer.
            /// </summary>
            /// <param name="x">The x co-ordinate of the value to get.</param>
            /// <param name="y">The y co-ordinate of the value to get.</param>
            /// <returns>The value at this location.</returns>
            public byte GetValue(int x, int y)
            {
                int seek = Seek(x, y);
                if (BitsPerValue == 8)
                {
                    return Buffer[seek];
                }
                else
                {
                    int index = seek / ValuesPerByte;
                    int vin = Buffer[index] >> (8 - BitsPerValue - BitsPerValue * (seek % ValuesPerByte));
                    int mask = 0xFF >> 8 - BitsPerValue;
                    return (byte)(vin & mask);
                }
            }
            /// <summary>
            /// Sets a value at the given location in the buffer. The given value should be less than or equal to
            /// <see cref="P:CsDesktopPonies.SpriteManagement.GifImage`1.DataBuffer.MaxValue"/>.
            /// </summary>
            /// <param name="x">The x co-ordinate of the value to set.</param>
            /// <param name="y">The y co-ordinate of the value to set.</param>
            /// <param name="value">The value to set at this location.</param>
            public void SetValue(int x, int y, byte value)
            {
                int seek = Seek(x, y);
                if (BitsPerValue == 8)
                {
                    Buffer[seek] = value;
                }
                else
                {
                    int index = seek / ValuesPerByte;
                    int shift = 8 - BitsPerValue - BitsPerValue * (seek % ValuesPerByte);
                    int vout = value << shift;
                    int mask = ((0xFF >> 8 - BitsPerValue) << shift) ^ 0xFF;
                    Buffer[index] &= (byte)mask;
                    Buffer[index] |= (byte)vout;
                }
            }
            /// <summary>
            /// Doubles the current <see cref="P:CsDesktopPonies.SpriteManagement.GifImage`1.DataBuffer.BitPerValue"/>, thus doubling the
            /// range of values that may be stored in each entry of the buffer.
            /// </summary>
            /// <exception cref="T:System.InvalidOperationException">The number of bits used for each value is already at its maximum size.
            /// </exception>
            public void UpsizeBuffer()
            {
                if (BitsPerValue == 8)
                    throw new InvalidOperationException("The BitsPerValue for the buffer is already at its maximum of 8.");

                byte newBitsPerValue = (byte)(BitsPerValue * 2);
                DataBuffer newDataBuffer = new DataBuffer(Size, newBitsPerValue);

                for (int y = 0; y < Size.Height; y++)
                    for (int x = 0; x < Size.Width; x++)
                        newDataBuffer.SetValue(x, y, GetValue(x, y));

                bitsPerValue = newBitsPerValue;
                Stride = newDataBuffer.Stride;
                Buffer = newDataBuffer.Buffer;
            }
            /// <summary>
            /// Makes this <see cref="T:CsDesktopPonies.SpriteManagement.GifImage`1.DataBuffer"/> equivalent to <paramref name="buffer"/>.
            /// </summary>
            /// <param name="buffer">The <see cref="T:CsDesktopPonies.SpriteManagement.GifImage`1.DataBuffer"/> whose data should be copied
            /// into this buffer.</param>
            public void MakeEqual(DataBuffer buffer)
            {
                while (buffer.BitsPerValue > bitsPerValue)
                    UpsizeBuffer();

                Array.Copy(buffer.Buffer, Buffer, Buffer.Length);
            }
            /// <summary>
            /// Enumerates through each lookup value in the frame buffer.
            /// </summary>
            /// <returns>An <see cref="T:System.Collections.Generic.IEnumerable`1"/> that returns each lookup value in the frame buffer.
            /// </returns>
            public IEnumerable<byte> EnumerateValues()
            {
                if (ValuesPerByte == 1)
                {
                    // Values are byte aligned.
                    for (int i = 0; i < Buffer.Length; i++)
                        yield return Buffer[i];
                }
                else if (Stride == Size.Width)
                {
                    // Values are packed, and so must be unpacked.
                    byte mask = (byte)(0xFF >> 8 - BitsPerValue);
                    for (int i = 0; i < Buffer.Length; i++)
                    {
                        byte value = Buffer[i];
                        for (byte valueInByte = 0; valueInByte < ValuesPerByte; valueInByte++)
                        {
                            yield return (byte)(value & mask);
                            value >>= BitsPerValue;
                        }
                    }
                }
                else
                {
                    // Values are packed, and we must account for some padding at the end of each row which should be skipped.
                    int x = 0;
                    int y = 0;
                    byte mask = (byte)(0xFF >> 8 - BitsPerValue);
                    for (int i = 0; i < Buffer.Length; i++)
                    {
                        byte value = Buffer[i];
                        for (byte valueInByte = 0; valueInByte < ValuesPerByte; valueInByte++)
                        {
                            yield return (byte)(value & mask);
                            value >>= BitsPerValue;
                            if (++x >= Size.Width)
                            {
                                x = 0;
                                y++;
                                // Skip the padding bits at the end of the row.
                                break;
                            }
                        }
                    }
                }
            }
        }
        #endregion

        #region LogicalScreenDescriptor class
        /// <summary>
        /// Provides a description of the image dimensions and global color table.
        /// </summary>
        private class LogicalScreenDescriptor
        {
            /// <summary>
            /// Gets the dimensions, in pixels, of the logical screen.
            /// </summary>
            public Size Size { get; private set; }
            /// <summary>
            /// Gets the area, in pixels, of the logical screen.
            /// </summary>
            public int Area
            {
                get { return Size.Width * Size.Height; }
            }
            /// <summary>
            /// Gets a value indicating whether a global color table exists. If true, the
            /// <see cref="P:CsDesktopPonies.SpriteManagement.GifImage`1.LogicalScreenDescriptor.BackgroundIndex"/> property is meaningful.
            /// </summary>
            public bool GlobalTableExists { get; private set; }
            /// <summary>
            /// Gets the number of bits available for each color in the original image.
            /// </summary>
            public byte OriginalBitsPerColor { get; private set; }
            /// <summary>
            /// Gets a value indicating whether the global color table is sorted. If true, colors are listed in decreasing order of
            /// importance; otherwise, no ordering is defined.
            /// </summary>
            public bool GlobalTableSorted { get; private set; }
            /// <summary>
            /// Gets the number of colors in the global color table.
            /// </summary>
            public int GlobalTableSize { get; private set; }
            /// <summary>
            /// Gets the index of the background color in the global color table. Only meaningful if
            /// <see cref="P:CsDesktopPonies.SpriteManagement.GifImage`1.LogicalScreenDescriptor.GlobalTableExists"/> is true.
            /// </summary>
            public byte BackgroundIndex { get; private set; }
            /// <summary>
            /// Gets an approximation of the aspect ratio of the original image. If null, no information exists.
            /// </summary>
            public float? ApproximateAspectRatio { get; private set; }

            /// <summary>
            /// Initializes a new instance of the <see cref="T:CsDesktopPonies.SpriteManagement.GifImage`1.LogicalScreenDescriptor"/>
            /// class.
            /// </summary>
            /// <param name="logicalScreenWidth">The width of the logical screen.</param>
            /// <param name="logicalScreenHeight">The height of the logical screen.</param>
            /// <param name="globalColorTableFlag">Indicates if a global color table is present.</param>
            /// <param name="colorResolution">The original color resolution of the image.</param>
            /// <param name="sortFlag">Indicates if the global color table is sorted.</param>
            /// <param name="sizeOfGlobalColorTable">The number of colors in the global color table.</param>
            /// <param name="backgroundColorIndex">The index of the background color in the global color table.</param>
            /// <param name="pixelAspectRatio">The aspect ratio of the pixel dimensions of the image.</param>
            internal LogicalScreenDescriptor(ushort logicalScreenWidth, ushort logicalScreenHeight,
                bool globalColorTableFlag, byte colorResolution, bool sortFlag,
                int sizeOfGlobalColorTable, byte backgroundColorIndex, byte pixelAspectRatio)
            {
                Size = new Size(logicalScreenWidth, logicalScreenHeight);
                GlobalTableExists = globalColorTableFlag;
                OriginalBitsPerColor = colorResolution;
                GlobalTableSorted = sortFlag;
                GlobalTableSize = sizeOfGlobalColorTable;
                BackgroundIndex = backgroundColorIndex;
                if (pixelAspectRatio == 0)
                    ApproximateAspectRatio = null;
                else
                    ApproximateAspectRatio = (float)(pixelAspectRatio + 15) / 64f;
            }
        }
        #endregion

        #region GraphicControlExtension class
        /// <summary>
        /// Provides a description of how subframes should be layered, the use of transparency and frame delay.
        /// </summary>
        private class GraphicControlExtension
        {
            /// <summary>
            /// Gets the method by which pixels should be overwritten.
            /// </summary>
            public DisposalMethod DisposalMethod { get; private set; }
            /// <summary>
            /// Gets a value indicating whether user input is expected.
            /// </summary>
            public bool UserInputExpected { get; private set; }
            /// <summary>
            /// Gets a value indicating whether transparency is used. If true, indexes matching the
            /// <see cref="P:CsDesktopPonies.SpriteManagement.GifImage`1.GraphicControlExtension.TransparentIndex"/> should be ignored.
            /// </summary>
            public bool TransparencyUsed { get; private set; }
            /// <summary>
            /// Gets the delay for this frame, in milliseconds, which indicates how much time should pass before the next frame is
            /// rendered.
            /// </summary>
            public int Delay { get; private set; }
            /// <summary>
            /// Gets the index of the transparent color in the color table. Only meaningful if
            /// <see cref="P:CsDesktopPonies.SpriteManagement.GifImage`1.GraphicControlExtension.TransparentUsed"/> is true.
            /// </summary>
            public byte TransparentIndex { get; private set; }

            /// <summary>
            /// Initializes a new instance of the <see cref="T:CsDesktopPonies.SpriteManagement.GifImage`1.GraphicControlExtension"/>
            /// class.
            /// </summary>
            /// <param name="disposalMethod">The <see cref="T:CsDesktopPonies.SpriteManagement.GifImage`1.DisposalMethod"/> to be used
            /// after this subframe is rendered.</param>
            /// <param name="userInputFlag">Indicates if user input is expected.</param>
            /// <param name="transparencyFlag">Indicates if transparency is used. If true the <paramref name="transparentColorIndex"/>
            /// should be treated as transparent.</param>
            /// <param name="delayTime">The delay, in hundredths of a second, before the next frame should be rendered.</param>
            /// <param name="transparentColorIndex">The index of the color to treat as transparent, if the
            /// <paramref name="transparencyFlag"/> is set.</param>
            /// <exception cref="System.ComponentModel.InvalidEnumArgumentException"><paramref name="disposalMethod"/> is invalid.
            /// </exception>
            internal GraphicControlExtension(
                DisposalMethod disposalMethod, bool userInputFlag, bool transparencyFlag, ushort delayTime, byte transparentColorIndex)
            {
                Argument.EnsureEnumIsValid(disposalMethod, "disposalMethod");

                DisposalMethod = disposalMethod;
                UserInputExpected = userInputFlag;
                TransparencyUsed = transparencyFlag;
                Delay = delayTime * 10;
                TransparentIndex = transparentColorIndex;
            }
        }
        #endregion

        #region ImageDescriptor class
        /// <summary>
        /// Provides a description of the subframe location and dimensions, and the local color table.
        /// </summary>
        private class ImageDescriptor
        {
            /// <summary>
            /// Gets the location and dimensions, in pixels, of the subframe area.
            /// </summary>
            public Rectangle Subframe { get; private set; }
            /// <summary>
            /// Gets a value indicating whether a local color table exists.
            /// </summary>
            public bool LocalTableExists { get; private set; }
            /// <summary>
            /// Gets a value indicating whether the subframe image is interlaced in a four-pass interlace pattern.
            /// </summary>
            public bool Interlaced { get; private set; }
            /// <summary>
            /// Gets a value indicating whether the local color table is sorted. If true, colors are listed in decreasing order of
            /// importance; otherwise, no ordering is defined.
            /// </summary>
            public bool LocalTableSorted { get; private set; }
            /// <summary>
            /// Gets the number of colors in the local color table.
            /// </summary>
            public int LocalTableSize { get; private set; }

            /// <summary>
            /// Initializes a new instance of the <see cref="T:CsDesktopPonies.SpriteManagement.GifImage`1.ImageDescriptor"/> class.
            /// </summary>
            /// <param name="imageLeftPosition">The left position of the subframe.</param>
            /// <param name="imageTopPosition">The top position of the subframe.</param>
            /// <param name="imageWidth">The width of the subframe.</param>
            /// <param name="imageHeight">The height of the subframe.</param>
            /// <param name="localColorTableFlag">Indicates if a local color table is present.</param>
            /// <param name="interlaceFlag">Indicates if the image is interlaced.</param>
            /// <param name="sortFlag">Indicates if the local color table is sorted.</param>
            /// <param name="sizeOfLocalColorTable">The number of colors in the local color table.</param>
            internal ImageDescriptor(ushort imageLeftPosition, ushort imageTopPosition, ushort imageWidth, ushort imageHeight,
                bool localColorTableFlag, bool interlaceFlag, bool sortFlag, int sizeOfLocalColorTable)
            {
                Subframe = new Rectangle(imageLeftPosition, imageTopPosition, imageWidth, imageHeight);
                LocalTableExists = localColorTableFlag;
                Interlaced = interlaceFlag;
                LocalTableSorted = sortFlag;
                LocalTableSize = sizeOfLocalColorTable;
            }
        }
        #endregion

        #region DisposalMethod enum
        /// <summary>
        /// Defines how pixels are to be overwritten.
        /// </summary>
        private enum DisposalMethod : byte
        {
            /// <summary>
            /// Undefined and should not be used. If encountered, treat as DoNotDispose.
            /// </summary>
            Undefined = 0,
            /// <summary>
            /// Keep the current buffer, later frames will layer above it.
            /// </summary>
            DoNotDispose = 1,
            /// <summary>
            /// Clear the background within the sub-frame that was just rendered.
            /// </summary>
            RestoreBackground = 2,
            /// <summary>
            /// Reset the background within the sub-frame that was just rendered to its previous values.
            /// </summary>
            RestorePrevious = 3,
        }
        #endregion

        #region Fields and Properties
        /// <summary>
        /// Gets the total duration of the image, in milliseconds.
        /// </summary>
        public int Duration { get; private set; }
        /// <summary>
        /// Gets the number of times this image plays. If 0, it loops indefinitely.
        /// </summary>
        public int Iterations { get; private set; }
        /// <summary>
        /// Gets the frames that make up this GIF image.
        /// </summary>
        public IList<GifFrame<T>> Frames { get; private set; }
        /// <summary>
        /// Gets the size of the image.
        /// </summary>
        public Size Size { get; private set; }
        /// <summary>
        /// Gets the width of the image.
        /// </summary>
        public int Width
        {
            get { return Size.Width; }
        }
        /// <summary>
        /// Gets the height of the image.
        /// </summary>
        public int Height
        {
            get { return Size.Height; }
        }

        /// <summary>
        /// Creates a frame of the desired type from the raw buffer.
        /// </summary>
        private BufferToImage<T> createFrame;
        /// <summary>
        /// The set of valid bit depths for use within buffers.
        /// </summary>
        private BitDepths validDepths;
        /// <summary>
        /// The description of the logical screen and global color table.
        /// </summary>
        private LogicalScreenDescriptor screenDescriptor;

        /// <summary>
        /// Buffer holding the information for the frame.
        /// </summary>
        private DataBuffer frameBuffer;
        /// <summary>
        /// Buffer holding information for the previous frame.
        /// </summary>
        private DataBuffer previousFrameBuffer;
        /// <summary>
        /// The global color table.
        /// </summary>
        private RgbColor[] globalColorTable;
        /// <summary>
        /// The color table currently in use.
        /// </summary>
        private RgbColor[] colorTable;
        /// <summary>
        /// The index in the color table being used for transparency across all frames.
        /// </summary>
        private byte imageTransparentIndex;
        /// <summary>
        /// The backup index in the color table whose color value matches that of the image transparent index.
        /// </summary>
        private byte imageTransparentIndexRemap;
        /// <summary>
        /// Indicates if any frame so far has used transparency, and thus there might be a transparent region in the buffer.
        /// </summary>
        private bool anyTransparencyUsed = true;

        // The values read in are variable length codewords, which can be of any length.
        // The gif specification sets a maximum code size of 12, and thus a maximum codeword count of 2^12 = 4096.
        // Thus we need to maintain a dictionary of up to 4096 codewords (which each can be of any length).
        // These codewords have a property that make them easy to store however.
        // If some word w plus some character c is in the dictionary, then the word w will be in the dictionary.
        // Thus the prefix word w can be used to lookup the character at the end of its length.
        // The suffix character c on this word maps to an actual value that is added to the pixel stack.
        /// <summary>
        /// The maximum number of codewords that can occur according to the GIF specification.
        /// </summary>
        private const int MaxCodeWords = 4096;
        /// <summary>
        /// The lookup array of "prefix" words. The value is the index of the "suffix" character at the end of the given codeword.
        /// </summary>
        private short[] prefix = new short[MaxCodeWords];
        /// <summary>
        /// The lookup array of "suffix" characters. This holds the actual decompressed byte value for the given code character.
        /// </summary>
        private byte[] suffix = new byte[MaxCodeWords];
        /// <summary>
        /// This array is used as a stack to store resulting pixel values. These values are the indexes in the color palette.
        /// </summary>
        private byte[] pixelStack = new byte[MaxCodeWords + 1];
        /// <summary>
        /// This array is a buffer to hold the data read in from an image data sub block.
        /// </summary>
        private byte[] block = new byte[256];
        #endregion

        /// <summary>
        /// Initializes a new instance of the <see cref="T:CsDesktopPonies.SpriteManagement.GifImage`1"/> class by decoding a GIF from the
        /// given stream.
        /// </summary>
        /// <param name="stream">A <see cref="T:System.IO.Stream"/> ready to read a GIF file.</param>
        /// <param name="imageFactory">The method used to construct an image of type <typeparamref name="T"/> from the decoded buffer.
        /// </param>
        /// <param name="allowableDepths">The allowable set of bit depths for the decoded buffer. Specify as many indexed formats as are
        /// supported by <typeparamref name="T"/>. If no such formats are supported, it is suggested you specify only the
        /// <see cref="F:CsDesktopPonies.SpriteManagement.BitDepths.Indexed8Bpp"/> format to make conversion easier. The
        /// <see cref="F:CsDesktopPonies.SpriteManagement.BitDepths.Indexed8Bpp"/> format must be specified.</param>
        /// <exception cref="T:System.ArgumentNullException"><paramref name="stream"/> is null.-or-<paramref name="imageFactory"/> is null.
        /// </exception>
        /// <exception cref="T:System.ComponentModel.InvalidEnumArgumentException"><paramref name="allowableDepths"/> is invalid.
        /// </exception>
        /// <exception cref="T:System.ArgumentException"><paramref name="stream"/> does not support reading.-or-
        /// <paramref name="allowableDepths"/> does not specify <see cref="F:CsDesktopPonies.SpriteManagement.BitDepths.Indexed8Bpp"/>.
        /// </exception>
        /// <exception cref="T:System.NotSupportedException">The file uses a feature of GIF that is not supported by the decoder.
        /// </exception>
        /// <exception cref="T:System.IO.InvalidDataException"><paramref name="stream"/> was not a GIF file, or was a badly formed GIF
        /// file.</exception>
        public GifImage(Stream stream, BufferToImage<T> imageFactory, BitDepths allowableDepths)
        {
            Argument.EnsureNotNull(stream, "stream");
            Argument.EnsureNotNull(imageFactory, "frameFactory");
            Argument.EnsureEnumIsValid(allowableDepths, "allowableDepths");
            if (!stream.CanRead)
                throw new ArgumentException("stream must support reading.", "stream");
            if (!allowableDepths.HasFlag(BitDepths.Indexed8Bpp))
                throw new ArgumentException("allowableDepths must support at least the Indexed8Bpp format.", "allowableDepths");

            createFrame = imageFactory;
            validDepths = allowableDepths;
            BinaryReader input = null;
            try
            {
                input = new BinaryReader(new BufferedStream(stream));
                DecodeGif(input);
            }
            finally
            {
                if (input != null)
                    input.Close();

                // Remove references to decoding fields so their memory can be reclaimed.
                createFrame = null;
                screenDescriptor = null;
                frameBuffer = null;
                previousFrameBuffer = null;
                globalColorTable = null;
                colorTable = null;
                prefix = null;
                suffix = null;
                pixelStack = null;
                block = null;
            }
        }
        /// <summary>
        /// Reads the color table and sets up the buffer and transparent indexes.
        /// </summary>
        /// <param name="input">The input stream.</param>
        /// <param name="tableSize">The size of the color table to read.</param>
        private void SetupColorTable(BinaryReader input, int tableSize)
        {
            colorTable = ReadColorTable(input, tableSize);
            if (frameBuffer == null)
            {
                // Create the initial buffers.
                DetermineTransparentIndexes();
                byte targetBpp = TargetBitsPerPixel(colorTable.Length);
                frameBuffer = new DataBuffer(Size, targetBpp, imageTransparentIndex);
                previousFrameBuffer = new DataBuffer(Size, targetBpp, imageTransparentIndex);
            }
            else
            {
                AdjustForChangedColorTable();
            }
        }
        /// <summary>
        /// Reads a color table block containing the given number of colors. These are optional blocks. Up to one global table may exist
        /// per data stream. Up to one local table may exist per image.
        /// </summary>
        /// <param name="input">The input stream.</param>
        /// <param name="colorCount">The number of colors in the table.</param>
        /// <returns>A new <see cref="T:CsDesktopPonies.SpriteManagement.RgbColor[]"/> containing the colors read in from the block.
        /// </returns>
        private static RgbColor[] ReadColorTable(BinaryReader input, int colorCount)
        {
            RgbColor[] colors = new RgbColor[colorCount];
            for (int i = 0; i < colors.Length; i++)
                colors[i] = new RgbColor(input.ReadByte(), input.ReadByte(), input.ReadByte());
            return colors;
        }
        /// <summary>
        /// Determines the indexes to use for transparency in the color table.
        /// </summary>
        private void DetermineTransparentIndexes()
        {
            // Find a pair of indexes in the color table with the same lookup value.
            // Start from the end of the table, since that's usually where all the uninitialized values live. Usually this means the loop
            // will return in the first iteration.
            bool found = false;
            for (int i = colorTable.Length - 1; i >= 1 && !found; i--)
                for (int j = i - 1; j >= 0 && !found; j--)
                    if (i != j && colorTable[i] == colorTable[j])
                    {
                        imageTransparentIndex = (byte)i;
                        imageTransparentIndexRemap = (byte)j;
                        found = true;
                    }

            if (!found)
            {
                // There were no duplicate indexes. But there is room to make the color table bigger and use the extra slot.
                if (colorTable.Length != 256)
                {
                    RgbColor[] newTable = new RgbColor[colorTable.Length + 1];
                    Array.Copy(colorTable, newTable, colorTable.Length);
                    colorTable = newTable;
                }

                // Use the last index in the new table, or if there was no room to expand the table, use the last index and just hope that
                // index was reserved for transparency or that transparency is not required.
                imageTransparentIndex = (byte)(colorTable.Length - 1);
                imageTransparentIndexRemap = (byte)(colorTable.Length - 1);
            }
        }
        /// <summary>
        /// Returns the number of bits per pixel that should be used in an indexed bitmap in order to accommodate the number of entries in
        /// the color table, whilst only using valid bit depths.
        /// </summary>
        /// <param name="colorCount">The number of colors in the color table.</param>
        /// <returns>The lowest number of bits that is an acceptable indexing format that can represent sufficient indexes for the color
        /// table.</returns>
        /// <exception cref="T:System.ArgumentException">The size of the color table is too large to index for any acceptable bit depth.
        /// </exception>
        private byte TargetBitsPerPixel(int colorCount)
        {
            byte minBbp = (byte)Math.Ceiling(Math.Log(colorCount, 2));
            byte candidateBbp = 1;
            while (candidateBbp < minBbp || (candidateBbp & (int)validDepths) == 0)
            {
                candidateBbp *= 2;
                if (candidateBbp > 8)
                    throw new ArgumentException("The size of the color table is too large to be indexed by any acceptable bit depth.");
            }
            return candidateBbp;
        }
        /// <summary>
        /// Adjusts the buffer sizes and transparent indexes to handle a change of color table.
        /// </summary>
        private void AdjustForChangedColorTable()
        {
            int oldImageTransparentIndex = imageTransparentIndex;
            DetermineTransparentIndexes();
            byte targetBpp = TargetBitsPerPixel(colorTable.Length);

            // Ensure the buffers can handle the new table size.
            while (frameBuffer.BitsPerValue < targetBpp)
                frameBuffer.UpsizeBuffer();
            while (previousFrameBuffer.BitsPerValue < targetBpp)
                previousFrameBuffer.UpsizeBuffer();

            // Remap any old transparent indexes to their new values.
            if (oldImageTransparentIndex != imageTransparentIndex)
                for (int x = 0; x < frameBuffer.Size.Width; x++)
                    for (int y = 0; y < frameBuffer.Size.Height; y++)
                    {
                        if (frameBuffer.GetValue(x, y) == oldImageTransparentIndex)
                            frameBuffer.SetValue(x, y, imageTransparentIndex);
                        if (previousFrameBuffer.GetValue(x, y) == oldImageTransparentIndex)
                            previousFrameBuffer.SetValue(x, y, imageTransparentIndex);
                    }
        }
        /// <summary>
        /// Decodes a GIF file from an input stream.
        /// </summary>
        /// <param name="input">The stream that is positioned to read the GIF file.</param>
        private void DecodeGif(BinaryReader input)
        {
            Iterations = 1;
            Frames = new List<GifFrame<T>>();
            ReadGifDataStream(input);
        }
        /// <summary>
        /// Reads the GIF data stream. This contains the header block, logical screen section, data sections, and trailer.
        /// </summary>
        /// <param name="input">The stream that is positioned to read the GIF file.</param>
        private void ReadGifDataStream(BinaryReader input)
        {
            // <GIF Data Stream> ::= Header <Logical Screen> <Data>* Trailer
            ReadHeader(input);
            ReadLogicalScreen(input);
            ReadDataAndTrailer(input);
        }
        /// <summary>
        /// Reads the header block (required - one per stream).
        /// </summary>
        /// <param name="input">The input stream.</param>
        private static void ReadHeader(BinaryReader input)
        {
            const string SignatureExpected = "GIF";
            string signature = new string(input.ReadChars(3));
            if (signature != SignatureExpected)
                throw new InvalidDataException(
                    string.Format(CultureInfo.CurrentCulture, "Invalid signature in header. Expected '{0}'. Read '{1}'.",
                    SignatureExpected, signature));

            const string Version87a = "87a";
            const string Version89a = "89a";
            string version = new string(input.ReadChars(3));
            if (version != Version87a && version != Version89a)
                throw new InvalidDataException(
                    string.Format(CultureInfo.CurrentCulture,  "Invalid version in header. Expected '{0}' or '{1}'. Read '{2}'.",
                    Version87a, Version89a, version));
        }
        /// <summary>
        /// Reads the logical screen section. This contains the logical screen descriptor and global color table.
        /// </summary>
        /// <param name="input">The input stream.</param>
        private void ReadLogicalScreen(BinaryReader input)
        {
            // <Logical Screen> ::= Logical Screen Descriptor [Global Color Table]
            screenDescriptor = ReadLogicalScreenDescriptor(input);
            Size = screenDescriptor.Size;
            ReadGlobalColorTable(input);
        }
        /// <summary>
        /// Reads the logical screen descriptor block. (required - one per stream).
        /// </summary>
        /// <param name="input">The input stream.</param>
        /// <returns>A new <see cref="T:CsDesktopPonies.SpriteManagement.GifLogicalScreenDescriptor"/> describing the logical screen.</returns>
        private static LogicalScreenDescriptor ReadLogicalScreenDescriptor(BinaryReader input)
        {
            ushort logicalScreenWidth = input.ReadUInt16();
            ushort logicalScreenHeight = input.ReadUInt16();

            byte packedFields = input.ReadByte();
            bool globalColorTableFlag = (packedFields & 0x80) != 0;
            byte colorResolution = (byte)(((packedFields & 0x70) >> 4) + 1);
            bool sortFlag = (packedFields & 0x08) >> 3 == 1;
            int globalBitsPerPixel = (packedFields & 0x07) + 1;
            int sizeOfGlobalColorTable = 1 << globalBitsPerPixel;
            byte backgroundColorIndex = input.ReadByte();
            byte pixelAspectRatio = input.ReadByte();

            return new LogicalScreenDescriptor(logicalScreenWidth, logicalScreenHeight, globalColorTableFlag, colorResolution,
                sortFlag, sizeOfGlobalColorTable, backgroundColorIndex, pixelAspectRatio);
        }
        /// <summary>
        /// Reads the global color table block. (optional - max one per stream).
        /// </summary>
        /// <param name="input">The input stream.</param>
        private void ReadGlobalColorTable(BinaryReader input)
        {
            if (screenDescriptor.GlobalTableExists)
            {
                SetupColorTable(input, screenDescriptor.GlobalTableSize);
                globalColorTable = colorTable;
            }
        }
        /// <summary>
        /// Reads the data sections (optional - no limits) and the trailer (required - one per stream).
        /// </summary>
        /// <param name="input">The input stream.</param>
        private void ReadDataAndTrailer(BinaryReader input)
        {
            // <GIF Data Stream> ::= Header <Logical Screen> <Data>* Trailer
            // <Data> ::= <Graphic Block> | <Special-Purpose Block>

            BlockCode blockCode;

            // <Graphic Block> ::= [Graphic Control Extension] <Graphic-Rendering Block>
            // <Special-Purpose Block> ::= Application Extension | Comment Extension
            // <Graphic-Rendering Block> ::= <Table-Based Image> | Plain Text Extension
            // <Table-Based Image> ::= Image Descriptor [Local Color Table] Image Data

            do
            {
                blockCode = (BlockCode)input.ReadByte();
                switch (blockCode)
                {
                    case BlockCode.ImageDescriptor:
                        ReadTableBasedImage(input, null);
                        break;
                    case BlockCode.Extension:
                        ExtensionLabel extensionLabel = (ExtensionLabel)input.ReadByte();
                        switch (extensionLabel)
                        {
                            case ExtensionLabel.GraphicControl:
                                ReadGraphicBlock(input);
                                break;
                            case ExtensionLabel.Application:
                                ReadApplicationExtension(input);
                                break;
                            default:
                                SkipExtensionBlock(input);
                                break;
                        }
                        break;
                    case BlockCode.Trailer:
                        break;
                    default:
                        throw new InvalidDataException(
                            "Expected the start of a data section but block code did not match any known values.");
                }
            }
            while (blockCode != BlockCode.Trailer);
        }
        /// <summary>
        /// Reads a graphic block section. This contains a graphic control extension and a graphic-rendering block.
        /// </summary>
        /// <param name="input">The input stream.</param>
        private void ReadGraphicBlock(BinaryReader input)
        {
            // <Graphic Block> ::= [Graphic Control Extension] <Graphic-Rendering Block>
            // <Graphic-Rendering Block> ::= <Table-Based Image> | Plain Text Extension
            // <Table-Based Image> ::= Image Descriptor [Local Color Table] Image Data
            GraphicControlExtension graphicControl = ReadGraphicControlExtension(input);

            BlockCode blockCode = (BlockCode)input.ReadByte();
            ExtensionLabel extensionLabel = ExtensionLabel.GraphicControl;
            if (blockCode == BlockCode.Extension)
            {
                extensionLabel = (ExtensionLabel)input.ReadByte();
                if (extensionLabel != ExtensionLabel.PlainText)
                {
                    // We have some other extension here, we need to skip them until we meet something valid.
                    do
                    {
                        // Read block.
                        if (extensionLabel == ExtensionLabel.Application)
                            ReadApplicationExtension(input);
                        else
                            SkipExtensionBlock(input);

                        // Determine next block.
                        blockCode = (BlockCode)input.ReadByte();
                        if (blockCode == BlockCode.Extension)
                            extensionLabel = (ExtensionLabel)input.ReadByte();

                        // Continue until we have a valid start for a graphic-rendering block.
                    }
                    while (blockCode != BlockCode.ImageDescriptor && extensionLabel != ExtensionLabel.PlainText);
                }
            }

            // Read graphic rendering block.
            if (blockCode == BlockCode.ImageDescriptor)
            {
                // Read table based image data to finish the block.
                ReadTableBasedImage(input, graphicControl);
            }
            else if (extensionLabel == ExtensionLabel.PlainText)
            {
                // "Read" a plain text extension to finish the block.
                SkipExtensionBlock(input);
            }
            else
            {
                throw new InvalidDataException(
                    "Encountered an unexpected block code after reading the graphic control extension in a graphic block.");
            }
        }
        /// <summary>
        /// Reads a graphic control extension block (optional - max one preceding a graphic-rendering block).
        /// </summary>
        /// <param name="input">The input stream.</param>
        /// <returns>A new <see cref="T:CsDesktopPonies.SpriteManagement.GifImage`1.GraphicControlExtension"/> describing how a
        /// graphic-rendering block section is to be modified.</returns>
        private static GraphicControlExtension ReadGraphicControlExtension(BinaryReader input)
        {
            // Read block size.
            if (input.ReadByte() != 4)
                throw new InvalidDataException("Unexpected block length for a graphic control extension.");

            byte packedFields = input.ReadByte();
            DisposalMethod disposalMethod = (DisposalMethod)((packedFields & 0x1C) >> 2);
            bool userInputFlag = (packedFields & 0x02) != 0;
            bool transparencyFlag = (packedFields & 0x01) != 0;

            ushort delayTime = input.ReadUInt16();
            byte transparentColorIndex = input.ReadByte();

            // Read block terminator.
            input.ReadByte();

            return new GraphicControlExtension(disposalMethod, userInputFlag, transparencyFlag, delayTime, transparentColorIndex);
        }
        /// <summary>
        /// Reads an application extension block.
        /// </summary>
        /// <param name="input">The input stream.</param>
        private void ReadApplicationExtension(BinaryReader input)
        {
            // Read block size.
            if (input.ReadByte() != 11)
                throw new InvalidDataException("Unexpected block length for an application extension.");

            string applicationIdentifier = new string(input.ReadChars(8));
            string applicationAuthenticationCode = new string(input.ReadChars(3));

            if (applicationIdentifier == "NETSCAPE" && applicationAuthenticationCode == "2.0")
                ReadNetscapeApplicationExtension(input);
            else
                SkipDataSubBlocks(input);
        }
        /// <summary>
        /// Reads the Netscape application extension sub-block, which defines an iteration count for the file.
        /// </summary>
        /// <param name="input">The input stream.</param>
        private void ReadNetscapeApplicationExtension(BinaryReader input)
        {
            // Read block size.
            if (input.ReadByte() != 3)
                throw new InvalidDataException("Unexpected block length for the Netscape application extension.");

            // Read empty byte.
            input.ReadByte();

            // Read iteration count.
            Iterations = input.ReadUInt16();

            // Read sub-block terminator.
            input.ReadByte();
        }
        /// <summary>
        /// Reads a series of data sub blocks until a terminator is encountered.
        /// </summary>
        /// <param name="input">The input stream.</param>
        private static void SkipDataSubBlocks(BinaryReader input)
        {
            byte blockSize;
            do
            {
                blockSize = input.ReadByte();
                input.ReadBytes(blockSize);
            }
            while (blockSize > 0);
        }
        /// <summary>
        /// Reads an extension block.
        /// </summary>
        /// <param name="input">The input stream.</param>
        private void SkipExtensionBlock(BinaryReader input)
        {
            // Read block size.
            byte blockSize = input.ReadByte();

            if (blockSize == 0)
                return;

            // Read block data.
            input.ReadBytes(blockSize);

            // Skip data sub blocks.
            SkipDataSubBlocks(input);
        }
        /// <summary>
        /// Reads the image descriptor block (required - one per image in the stream).
        /// </summary>
        /// <param name="input">The input stream.</param>
        /// <returns>A new <see cref="T:CsDesktopPonies.SpriteManagement.GifImage`1.ImageDescriptor"/> describing the subframe.</returns>
        private static ImageDescriptor ReadImageDescriptor(BinaryReader input)
        {
            ushort imageLeftPosition = input.ReadUInt16();
            ushort imageTopPosition = input.ReadUInt16();
            ushort imageWidth = input.ReadUInt16();
            ushort imageHeight = input.ReadUInt16();

            byte packedFields = input.ReadByte();
            bool localColorTableFlag = (packedFields & 0x80) != 0;
            bool interlaceFlag = (packedFields & 0x40) != 0;
            bool sortFlag = (packedFields & 0x20) != 0;
            int localBitsPerPixel = (packedFields & 0x07) + 1;
            int sizeOfLocalColorTable = 1 << localBitsPerPixel;

            return new ImageDescriptor(imageLeftPosition, imageTopPosition, imageWidth, imageHeight,
                localColorTableFlag, interlaceFlag, sortFlag, sizeOfLocalColorTable);
        }
        /// <summary>
        /// Reads a table based image section. This contains an image descriptor, local color table and image data.
        /// </summary>
        /// <param name="input">The input stream.</param>
        /// <param name="graphicControl">A <see cref="T:CsDesktopPonies.SpriteManagement.GifImage`1.GraphicControlExtension"/> specifying
        /// how the value from the subframe is to be applied. This is optional.</param>
        private void ReadTableBasedImage(BinaryReader input, GraphicControlExtension graphicControl)
        {
            // <Table-Based Image> ::= Image Descriptor [Local Color Table] Image Data
            ImageDescriptor imageDescriptor = ReadImageDescriptor(input);
            Rectangle frame = new Rectangle(Point.Empty, Size);
            if (!frame.Contains(imageDescriptor.Subframe))
                throw new InvalidDataException("Subframe area extends outside the logical screen area.");
            if (frame == imageDescriptor.Subframe && (graphicControl == null || !graphicControl.TransparencyUsed))
                anyTransparencyUsed = false;

            if (imageDescriptor.LocalTableExists)
            {
                // Use the local color table.
                ReadLocalColorTable(input, imageDescriptor);
            }
            else if (globalColorTable != null && colorTable != globalColorTable)
            {
                // Use the global color table.
                colorTable = globalColorTable;
                AdjustForChangedColorTable();
            }
            else if (frameBuffer == null)
            {
                // No color tables have been defined. The image might be all-transparent. Set up a minimal buffer to start.
                imageTransparentIndex = 0;
                imageTransparentIndexRemap = 0;
                byte targetBpp = TargetBitsPerPixel(1);
                frameBuffer = new DataBuffer(Size, targetBpp, imageTransparentIndex);
                previousFrameBuffer = new DataBuffer(Size, targetBpp, imageTransparentIndex);
            }

            // Read the image data onto the frame buffer, then create the resulting image.
            ReadImageData(input, imageDescriptor, graphicControl);
            CreateFrame(graphicControl);

            // Apply the disposal method.
            if (graphicControl != null)
            {
                // DisposalMethod.Undefined is undefined, if encountered it is treated as DoNotDispose.
                // DisposalMethod.DoNotDispose indicates that the buffer should remain as it is, so do nothing.
                if (graphicControl.DisposalMethod == DisposalMethod.RestoreBackground)
                {
                    // Clear the background of the subframe area.
                    frameBuffer.FillBuffer(imageTransparentIndex, imageDescriptor.Subframe);
                    anyTransparencyUsed = true;
                }
                else if (graphicControl.DisposalMethod == DisposalMethod.RestorePrevious)
                {
                    // Fill the background with the previous frame.
                    frameBuffer.FillBuffer(previousFrameBuffer, imageDescriptor.Subframe);
                }
            }

            // The previous frame is now this frame.
            previousFrameBuffer.MakeEqual(frameBuffer);
        }
        /// <summary>
        /// Reads a local color table block. (optional - max one per image descriptor).
        /// </summary>
        /// <param name="input">The input stream.</param>
        /// <param name="imageDescriptor">The <see cref="T:CsDesktopPonies.SpriteManagement.GifImage`1.ImageDescriptor"/> which describes
        /// the table, and to which any table will belong.</param>
        private void ReadLocalColorTable(BinaryReader input, ImageDescriptor imageDescriptor)
        {
            if (imageDescriptor.LocalTableExists)
                SetupColorTable(input, imageDescriptor.LocalTableSize);
        }
        /// <summary>
        /// Reads image data onto the frame buffer. This is the LZW compressed information about the pixels in the subframe.
        /// </summary>
        /// <param name="input">The input stream.</param>
        /// <param name="imageDescriptor">An <see cref="T:CsDesktopPonies.SpriteManagement.GifImage`1.ImageDescriptor"/> describing the
        /// subframe.</param>
        /// <param name="graphicControl">A <see cref="T:CsDesktopPonies.SpriteManagement.GifImage`1.GraphicControlExtension"/> specifying
        /// how the value from the subframe is to be applied. This is optional.</param>
        private void ReadImageData(BinaryReader input, ImageDescriptor imageDescriptor, GraphicControlExtension graphicControl)
        {
            byte lzwMinimumCodeSize = input.ReadByte();

            #region Initialize decoder.
            // Image pixel position data.
            int width = imageDescriptor.Subframe.Width;
            int height = imageDescriptor.Subframe.Height;
            int x = 0;
            int y = 0;
            int interlacePass = 1;
            int yIncrement = imageDescriptor.Interlaced ? 8 : 1;

            // Indicates a null codeword.
            const int NullCode = -1;
            // The clear code is the first available unused code. We will use values below this as root characters in the dictionary.
            int clearCode = 1 << lzwMinimumCodeSize;
            // This code indicates the end of LZW compressed information.
            int endOfInformation = clearCode + 1;
            // This index indicates the first unused index in the dictionary.
            int available = clearCode + 2;
            // The size of codes being read in, in bits.
            int codeSize = lzwMinimumCodeSize + 1;
            // The mask used to get the current code from the bit buffer.
            int codeMask = (1 << codeSize) - 1;
            // Stores the old codeword.
            int oldCode = NullCode;
            // Stores the root character at the end of a codeword.
            byte rootCode = 0;

            // Variables managing the data block buffer from which data is read in.
            int blockIndex = 0;
            int bytesLeftInBlock = 0;
            int bitsBuffered = 0;
            int bitBuffer = 0;
            // Variable for the next available index in the stack.
            int stackIndex = 0;

            // Initialize table with root characters.
            for (int i = 0; i < clearCode; i++)
            {
                prefix[i] = 0;
                suffix[i] = (byte)i;
            }
            #endregion

            #region Decode GIF pixel stream.
            bool skipDataSubBlocks = true;
            for (int pixelIndex = 0; pixelIndex < width * height; )
            {
                // Read some values into the pixel stack as it is empty.
                if (stackIndex == 0)
                {
                    #region Read in another byte from the block buffer if we need more bits.
                    if (bitsBuffered < codeSize)
                    {
                        // Read in a new data block if we exhausted the block buffer.
                        if (bytesLeftInBlock == 0)
                        {
                            // Read a new data block.
                            bytesLeftInBlock = input.ReadByte();
                            block = input.ReadBytes(bytesLeftInBlock);
                            blockIndex = 0;

                            // If we happen to read the block terminator, we are done reading image data.
                            if (bytesLeftInBlock == 0)
                            {
                                // No need to skip remaining blocks, since we happened to read the terminator ourselves.
                                skipDataSubBlocks = false;
                                break;
                            }
                        }
                        bitBuffer += (int)block[blockIndex] << bitsBuffered;
                        bitsBuffered += 8;
                        blockIndex++;
                        bytesLeftInBlock--;
                        continue;
                    }
                    #endregion

                    // Get the next code from out bit buffer.
                    int code = bitBuffer & codeMask;
                    bitBuffer >>= codeSize;
                    bitsBuffered -= codeSize;

                    #region Interpret special codes.
                    // Indicates the end of input data.
                    if ((code > available) || (code == endOfInformation))
                        break;

                    // A clear code means the decoder dictionary should be reset.
                    if (code == clearCode)
                    {
                        codeSize = lzwMinimumCodeSize + 1;
                        codeMask = (1 << codeSize) - 1;
                        available = clearCode + 2;
                        oldCode = NullCode;
                        continue;
                    }

                    // Record the first code after an initialization/reset.
                    if (oldCode == NullCode)
                    {
                        pixelStack[stackIndex++] = suffix[code];
                        oldCode = code;
                        rootCode = (byte)code;
                        continue;
                    }
                    #endregion

                    // Save the code we read in.
                    // We will be using the code variable as an indexer to move through the word.
                    int inCode = code;

                    #region Get codeword values and push them to the stack.
                    // Handle the case where a code word was not yet defined. This only happens in one special case.
                    // Given string s and character c, this happens only when we see the sequence s.c.s.c.s and the word s.c was already in
                    // the dictionary beforehand.
                    if (code == available)
                    {
                        pixelStack[stackIndex++] = rootCode;
                        code = oldCode;
                    }
                    // Enumerate through the code word and push values to the stack.
                    while (code > clearCode)
                    {
                        pixelStack[stackIndex++] = suffix[code];
                        code = prefix[code];
                    }
                    // Record and push the root character to the stack.
                    rootCode = suffix[code];
                    pixelStack[stackIndex++] = rootCode;
                    #endregion

                    #region Add a new codeword to the dictionary.
                    // Add words to the dictionary whilst it is not full.
                    // If it is full, we keep the same dictionary until the encoder sends a clear code.
                    if (available < MaxCodeWords)
                    {
                        // Add a new word to the dictionary.
                        prefix[available] = (short)oldCode;
                        suffix[available] = rootCode;
                        available++;

                        // Increase the code size if the number of available codes now exceeds the number addressable by the current size.
                        if (available < MaxCodeWords && (available & codeMask) == 0)
                        {
                            codeSize++;
                            codeMask += available;
                        }
                    }
                    #endregion

                    // Note the previous codeword.
                    oldCode = inCode;
                }

                #region Pop a pixel off the pixel stack.
                // Apply pixel to our buffers.
                ApplyPixelToFrame(x, y, pixelStack[--stackIndex], imageDescriptor, graphicControl);
                pixelIndex++;

                // Move right one pixel.
                x++;
                if (x >= width)
                {
                    // Move to next row. If we're not interlacing this just means the next row.
                    // If interlacing, we must fill in every 8th row, then every 4th, then every 2nd then every other row.
                    x = 0;
                    y += yIncrement;

                    // If we reached the end of this interlacing pass, go back to the top and fill in every row between the current rows.
                    if (imageDescriptor.Interlaced && y >= height)
                    {
                        #region Choose next interlacing line.
                        do
                        {
                            interlacePass++;
                            switch (interlacePass)
                            {
                                case 2:
                                    y = 4;
                                    break;
                                case 3:
                                    y = 2;
                                    yIncrement = 4;
                                    break;
                                case 4:
                                    y = 1;
                                    yIncrement = 2;
                                    break;
                            }
                        }
                        while (y >= height);
                        #endregion
                    }
                }
                #endregion
            }
            #endregion

            // Read block terminator (and any trailing sub-blocks, though there should not be any).
            if (skipDataSubBlocks)
                SkipDataSubBlocks(input);
        }
        /// <summary>
        /// Uses a value from the subframe and applies it onto the frame buffer.
        /// </summary>
        /// <param name="x">The x co-ordinate, relative to the subframe bounds, where the pixel is to be applied.</param>
        /// <param name="y">The y co-ordinate, relative to the subframe bounds, where the pixel is to be applied.</param>
        /// <param name="pixel">The value to be applied, in accordance with the <paramref name="graphicControl"/> parameters if specified.
        /// </param>
        /// <param name="imageDescriptor">An <see cref="T:CsDesktopPonies.SpriteManagement.GifImage`1.ImageDescriptor"/> describing the
        /// subframe.</param>
        /// <param name="graphicControl">A <see cref="T:CsDesktopPonies.SpriteManagement.GifImage`1.GraphicControlExtension"/> specifying
        /// how the value from the subframe is to be applied. This is optional.</param>
        private void ApplyPixelToFrame(int x, int y, byte pixel, ImageDescriptor imageDescriptor, GraphicControlExtension graphicControl)
        {
            // Do not set a transparent pixel.
            if (graphicControl != null && graphicControl.TransparencyUsed && pixel == graphicControl.TransparentIndex)
                return;

            // Check the pixel does not conflict with the choice of transparency index.
            if (pixel != imageTransparentIndex)
            {
                // No conflict, so check the value is in range and set the pixel.
                CheckValueInRange(pixel);
                frameBuffer.SetValue(imageDescriptor.Subframe.X + x, imageDescriptor.Subframe.Y + y, pixel);
            }
            else if (imageTransparentIndex != imageTransparentIndexRemap)
            {
                // Use the backup index (whose color value is the same) if one exists.
                frameBuffer.SetValue(imageDescriptor.Subframe.X + x, imageDescriptor.Subframe.Y + y, imageTransparentIndexRemap);
            }
            else
            {
                // There is no backup index. This happens when the table was at size 256 and there were no duplicate colors.
                throw new NotSupportedException(string.Format(CultureInfo.CurrentCulture,
                    "Attempted to decode an image using 256 RGB colors and transparency. " +
                    "If transparency is required, this decoder only supports 255 RGB colors."));
            }
        }
        /// <summary>
        /// Checks a lookup value is without the limits for the current table size, otherwise throws an
        /// <see cref="T:System.IO.InvalidDataException"/>.
        /// </summary>
        /// <param name="value">The value to check.</param>
        /// <exception cref="T:System.IO.InvalidDataException">Thrown when <paramref name="value"/> is greater than or equal to the size of
        /// the color table.</exception>
        private void CheckValueInRange(byte value)
        {
            if (value >= colorTable.Length)
                throw new InvalidDataException(string.Format(CultureInfo.CurrentCulture,
                        "Indexed value of {0} was larger that the maximum of {1}.", value, colorTable.Length - 1));
        }
        /// <summary>
        /// Creates the frame image using the current buffer.
        /// </summary>
        /// <param name="graphicControl">A <see cref="T:CsDesktopPonies.SpriteManagement.GifImage`1.GraphicControlExtension"/> specifying
        /// how the value from the subframe is to be applied. This is optional.</param>
        private void CreateFrame(GraphicControlExtension graphicControl)
        {
            // Make a copy of the current frame buffer and color table, since we'll be reusing those arrays for later frames.
            byte[] bufferCopy = new byte[frameBuffer.Buffer.Length];
            Array.Copy(frameBuffer.Buffer, bufferCopy, frameBuffer.Buffer.Length);
            RgbColor[] tableCopy;
            if (colorTable != null)
            {
                tableCopy = new RgbColor[colorTable.Length];
                Array.Copy(colorTable, tableCopy, colorTable.Length);
            }
            else
            {
                tableCopy = new RgbColor[0];
            }

            // Create the frame image, and then the frame itself.
            int frameTransparentIndex = anyTransparencyUsed ? imageTransparentIndex : -1;
            int hashCode = GetFrameHash(tableCopy, frameTransparentIndex);
            T frame =
                createFrame(bufferCopy, tableCopy, frameTransparentIndex,
                frameBuffer.Stride, frameBuffer.Size.Width, frameBuffer.Size.Height, frameBuffer.BitsPerValue, hashCode);
            int delay = graphicControl != null ? graphicControl.Delay : 0;
            GifFrame<T> newFrame = new GifFrame<T>(frame, delay, tableCopy, frameTransparentIndex);
            Frames.Add(newFrame);
            Duration += newFrame.Duration;
        }
        /// <summary>
        /// Gets a hash code for the current frame, based on the attributes of the frame buffer.
        /// </summary>
        /// <param name="colors">The non-null array of colors used in the frame.</param>
        /// <param name="transparentIndex">The index of the transparent color in the palette.</param>
        /// <returns>A hash code for the current frame.</returns>
        private int GetFrameHash(RgbColor[] colors, int transparentIndex)
        {
            // Generate a quick hash just using the raw buffer values. This means some visually identical frames could hash differently if
            // the underlying buffer and color table conspire sufficiently.
            int hash = Fnv1AHash32(
                frameBuffer.Buffer
                .Concat(colors.SelectMany(color => BitConverter.GetBytes(color.ToArgb())))
                .Concat(BitConverter.GetBytes(transparentIndex))
                .Concat(BitConverter.GetBytes(frameBuffer.Size.Width))
                .Concat(BitConverter.GetBytes(frameBuffer.Size.Height)));

            //// Generate a hash code based on the resulting visual. Images which look the same will have the same code, even if their
            //// underlying buffers and lookup indexes are different.
            //int hash = Fnv1AHash32(
            //    frameBuffer.FrameValues().SelectMany(colorIndex =>
            //    {
            //        // Default value is the ARGB code for transparent black.
            //        int value = 0;
            //        if (colorIndex != transparentIndex)
            //            value = colors[colorIndex].ToArgb();
            //        return BitConverter.GetBytes(value);
            //    })
            //    .Concat(BitConverter.GetBytes(frameBuffer.Size.Width))
            //    .Concat(BitConverter.GetBytes(frameBuffer.Size.Height)));

            //// Hash the ARGB color code for every lookup value, and also the dimensions of the image.
            //const int transparentBlackArgb = 0;
            //var inputSequence = frameBuffer.EnumerateValues()
            //    .Select(colorIndex => colorIndex == transparentIndex ? transparentBlackArgb : colors[colorIndex].ToArgb())
            //    .Concat(new int[] { frameBuffer.Size.Width, frameBuffer.Size.Height });
            //int hash = 0x411C9DC5;
            //foreach (int input in inputSequence)
            //{
            //    // Mixing function lifted from the internet.
            //    int i = input;
            //    i = ~i + (i << 15); // key = (key << 15) - key - 1;
            //    i = i ^ (i >> 12);
            //    i = i + (i << 2);
            //    i = i ^ (i >> 4);
            //    i = i * 2057; // key = (key + (key << 3)) + (key << 11);
            //    i = i ^ (i >> 16);
            //    hash ^= i;
            //}

            return hash;
        }
        /// <summary>
        /// Gets the 32-bit FNV-1a hash code for a sequence of bytes.
        /// </summary>
        /// <param name="input">The sequence of bytes which should be hashed.</param>
        /// <returns>A 32-bit integer that is the hash code for the input bytes.</returns>
        private static int Fnv1AHash32(IEnumerable<byte> input)
        {
            const uint OffsetBasis32 = 2166136261;
            const uint FnvPrime32 = 16777619;

            uint hash = OffsetBasis32;
            foreach (byte octet in input)
            {
                hash ^= octet;
                hash *= FnvPrime32;
            }
            return (int)hash;
        }
    }

    /// <summary>
    /// Defines a single frame in a GIF animation.
    /// </summary>
    /// <typeparam name="T">The type of the frame image.</typeparam>
    public sealed class GifFrame<T>
    {
        /// <summary>
        /// Gets the image for this frame.
        /// </summary>
        public T Image { get; private set; }
        /// <summary>
        /// Gets the duration of this frame, in milliseconds.
        /// </summary>
        public int Duration { get; private set; }
        /// <summary>
        /// Gets the size of the color table.
        /// </summary>
        public int ColorTableSize
        {
            get { return colorTable.Length; }
        }
        /// <summary>
        /// The color table used for the image.
        /// </summary>
        private RgbColor[] colorTable;
        /// <summary>
        /// The index of the transparent color in the <see cref="F:CsDesktopPonies.SpriteManagement.GifFrame`1.colorTable"/>, or -1 to
        /// indicate no transparent color.
        /// </summary>
        private int transparentIndex;

        /// <summary>
        /// Initializes a new instance of the <see cref="T:CsDesktopPonies.SpriteManagement.GifFrame`1"/> class.
        /// </summary>
        /// <param name="frame">The image for this frame.</param>
        /// <param name="duration">The duration of this frame, in milliseconds.</param>
        /// <param name="colorTable">The color table used for the image.</param>
        /// <param name="transparentIndex">The index of the transparent color in <paramref name="colorTable"/>, or -1 if there is no
        /// transparent color.</param>
        internal GifFrame(T frame, int duration, RgbColor[] colorTable, int transparentIndex)
        {
            Image = frame;
            Duration = duration;
            this.colorTable = colorTable;
            this.transparentIndex = transparentIndex;
        }
        /// <summary>
        /// Gets a copy of the color table used for the image.
        /// </summary>
        /// <returns>A copy of the color table used for the image.</returns>
        public ArgbColor[] GetColorTable()
        {
            ArgbColor[] colors = new ArgbColor[ColorTableSize];
            for (int i = 0; i < ColorTableSize; i++)
                colors[i] = new ArgbColor(255, colorTable[i]);

            if (transparentIndex != -1)
                colors[transparentIndex] = new ArgbColor(0, colorTable[transparentIndex]);

            return colors;
        }
    }
}