namespace OmegaLauncher.Updates.Client
{
    /// <summary>
    /// Reads the FileVersion of a Windows executable straight from its bytes (the VS_FIXEDFILEINFO block), so it works on Linux without Wine
    /// and without any Windows API.
    /// </summary>
    public static class PeVersion
    {
        // VS_FIXEDFILEINFO starts with the signature 0xFEEF04BD (little endian: BD 04 EF FE).
        private static readonly byte[] Signature = { 0xBD, 0x04, 0xEF, 0xFE };

        /// <summary>The version as "major.minor.build.revision", or null if the file is not a readable executable.</summary>
        public static string Read(string path)
        {
            try
            {
                using FileStream fs = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 20, FileOptions.SequentialScan);

                byte[] header = new byte[2];
                if (fs.Read(header, 0, 2) != 2 || header[0] != 'M' || header[1] != 'Z')
                    return null;

                const int chunkSize = 4 << 20;
                const int overlap = 32;
                byte[] buffer = new byte[chunkSize + overlap];
                long position = 0;
                long length = fs.Length;

                while (position < length)
                {
                    fs.Seek(position, SeekOrigin.Begin);
                    int read = fs.Read(buffer, 0, buffer.Length);
                    if (read <= 0)
                        break;

                    int index = 0;
                    while ((index = IndexOf(buffer, read, index)) >= 0)
                    {
                        // Layout: signature, struct version, file version MS, file version LS.
                        if (index + 16 <= read)
                        {
                            uint structVersion = BitConverter.ToUInt32(buffer, index + 4);
                            if (structVersion == 0x10000 || structVersion == 0)
                            {
                                uint ms = BitConverter.ToUInt32(buffer, index + 8);
                                uint ls = BitConverter.ToUInt32(buffer, index + 12);
                                return $"{ms >> 16}.{ms & 0xFFFF}.{ls >> 16}.{ls & 0xFFFF}";
                            }
                        }

                        index++;
                    }

                    position += chunkSize;
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            return null;
        }

        private static int IndexOf(byte[] buffer, int length, int start)
        {
            for (int i = start; i <= length - Signature.Length; i++)
            {
                if (buffer[i] == Signature[0] && buffer[i + 1] == Signature[1] && buffer[i + 2] == Signature[2] && buffer[i + 3] == Signature[3])
                    return i;
            }

            return -1;
        }
    }
}
