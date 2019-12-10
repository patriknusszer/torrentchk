using System;
using System.Text;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace Nusstudios.Parsers
{
    public class TorrentParser
    {
        private static JObject CTICreateProcessingPieceStatus(int currentPiece, int totalPieces)
        {
            JObject status = new JObject();
            status["status"] = "processing_piece";
            JObject piece_processing = new JObject();
            piece_processing["current_piece"] = currentPiece;
            piece_processing["total_pieces"] = totalPieces;
            status["processing_piece"] = piece_processing;
            return status;
        }

        private static JObject CTICreateParsingStatus()
        {
            JObject status = new JObject();
            status["status"] = "parsing";
            return status;
        }

        // Check Torrent Integrity callback
        public delegate void CTICB(JObject status);

        private static void CTITryReportStatus(CTICB cb, JObject status)
        {
            if (cb != null)
            {
                cb(status);
            }
        }

        public TorrentParser(byte[] data)
        {
            tree = new JObject();
            int position = 0;
            ReadDictionary(ref data, ref position, null, ref tree);
            string pieces = tree["info"]["pieces"].ToString();
            pieceHashes = new string[pieces.Length / 40];

            for (int i = 0, x = 0; i < pieces.Length; i += 40, x++)
            {
                pieceHashes[x] = pieces.Substring(i, 40);
            }
        }

        public JObject tree;
        public string[] pieceHashes;

        public static bool CheckTorrentIntegrityV2(string torrentPath, string downloadPath, CTICB cb)
        {
            CTITryReportStatus(cb, CTICreateParsingStatus());
            TorrentParser torrentParser = new TorrentParser(File.ReadAllBytes(torrentPath));
            JObject _tree = torrentParser.tree;
            int pieceLength = (int)_tree["info"]["piece length"];
            JArray files = (JArray)_tree["info"]["files"];
            string name = (string)_tree["info"]["name"];
            int pieceHashesIndex = 0;
            CTITryReportStatus(cb, CTICreateProcessingPieceStatus(1, torrentParser.pieceHashes.Length));

            if (files != null)
            {
                int leftToRead = pieceLength;
                List<byte> piece = new List<byte>();

                for (int i = 0; i < files.Count; i++)
                {
                    JObject file = (JObject)files[i];
                    JArray path = (JArray)file["path"];
                    FileStream fileStream = new FileStream(ResolvePath(downloadPath, name, path), FileMode.Open);
                    long rest = fileStream.Length;

                    while (rest >= leftToRead)
                    {
                        byte[] buffer = new byte[leftToRead];
                        fileStream.Read(buffer, 0, leftToRead);
                        rest = (fileStream.Length - fileStream.Position);
                        piece.AddRange(buffer);
                        string pieceHash = torrentParser.pieceHashes[pieceHashesIndex];
                        pieceHashesIndex++;
                        SHA1 sha1 = SHA1.Create();
                        string hash = B2S(sha1.ComputeHash(piece.ToArray()));
                        piece.Clear();

                        if (!pieceHash.Equals(hash))
                        {
                            return false;
                        }

                        CTITryReportStatus(cb, CTICreateProcessingPieceStatus(pieceHashesIndex + 1, torrentParser.pieceHashes.Length));
                        leftToRead = pieceLength;
                    }

                    if (rest != 0)
                    {
                        leftToRead = leftToRead - (int)rest;
                        byte[] buffer = new byte[rest];
                        fileStream.Read(buffer, 0, (int)rest);
                        piece.AddRange(buffer);
                        rest = 0;

                        if (i == files.Count - 1)
                        {
                            Console.WriteLine(pieceLength);
                            Console.WriteLine(buffer.Length);
                            string pieceHash = torrentParser.pieceHashes[pieceHashesIndex];
                            pieceHashesIndex++;
                            SHA1 sha1 = SHA1.Create();
                            string hash = B2S(sha1.ComputeHash(piece.ToArray()));
                            piece.Clear();

                            if (!pieceHash.Equals(hash))
                            {
                                return false;
                            }
                        }
                    }

                    fileStream.Dispose();
                }

                return true;
            }
            else
            {
                FileStream fileStream = new FileStream(Path.Combine(downloadPath, (string)_tree["info"]["name"]), FileMode.Open);
                byte[] piece = new byte[pieceLength];
                int read = 0;

                while ((read = fileStream.Read(piece, 0, pieceLength)) == pieceLength)
                {
                    if (read != pieceLength)
                    {
                        byte[] tmp = new byte[read];
                        Array.Copy(piece, tmp, read);
                        piece = tmp;
                    }

                    string pieceHash = torrentParser.pieceHashes[pieceHashesIndex];
                    pieceHashesIndex++;
                    SHA1 sha1 = SHA1.Create();
                    string hash = B2S(sha1.ComputeHash(piece));

                    if (!pieceHash.Equals(hash))
                    {
                        return false;
                    }

                    if (read == pieceLength)
                    {
                        CTITryReportStatus(cb, CTICreateProcessingPieceStatus(pieceHashesIndex + 1, torrentParser.pieceHashes.Length));
                    }
                }

                return true;
            }
        }

        public static bool CheckTorrentIntegrity(string torrentPath, string downloadPath, CTICB cb)
        {
            CTITryReportStatus(cb, CTICreateParsingStatus());
            TorrentParser torrentParser = new TorrentParser(File.ReadAllBytes(torrentPath));

            for (int i = 0; i < torrentParser.pieceHashes.Length; i++)
            {
                CTITryReportStatus(cb, CTICreateProcessingPieceStatus(i + 1, torrentParser.pieceHashes.Length));
                byte[] piece = torrentParser.GetPieceFor(torrentParser.tree, i, downloadPath);
                SHA1 sha1 = SHA1.Create();
                string pieceHash = torrentParser.pieceHashes[i];
                string hash = B2S(sha1.ComputeHash(piece));

                if (!pieceHash.Equals(hash))
                {
                    return false;
                }
            }

            return true;
        }

        public byte[] GetPieceFor(JObject _tree, long pieceHashIndex, string downloadPath)
        {
            int pieceLength = (int)_tree["info"]["piece length"];
            long destinationOffset = (long)pieceLength * pieceHashIndex;
            long startOffset = 0;
            List<byte> piece = new List<byte>();

            JArray files = (JArray)_tree["info"]["files"];

            if (files != null) {
                for (int i = 0; i < files.Count; i++)
                {
                    JObject file = (JObject)files[i];
                    long length = (long)file["length"];
                    JArray path = (JArray)file["path"];
                    long endOffset = startOffset + length - 1;

                    if (endOffset >= destinationOffset)
                    {
                        long fileOffset = 0;

                        if (startOffset < destinationOffset)
                        {
                            fileOffset = destinationOffset - startOffset;
                        }

                        string fn = ResolvePath(downloadPath, (string)_tree["info"]["name"], path);
                        FileStream fileStream = new FileStream(fn, FileMode.Open);
                        fileStream.Seek(fileOffset, SeekOrigin.Begin);
                        int leftToRead = pieceLength - piece.Count;
                        long maxReadableFromFile = length - fileOffset;
                        byte[] buffer;

                        if (maxReadableFromFile >= leftToRead)
                        {
                            buffer = new byte[leftToRead];
                            fileStream.Read(buffer, 0, leftToRead);
                            fileStream.Dispose();
                            piece.AddRange(buffer);
                            break;
                        }
                        else
                        {
                            FileInfo fileInfo = new FileInfo(fn);
                            buffer = new byte[maxReadableFromFile];
                            fileStream.Read(buffer, 0, (int)maxReadableFromFile);
                            fileStream.Dispose();
                            piece.AddRange(buffer);
                        }
                    }

                    startOffset = endOffset + 1;
                }
            }
            else {
                FileStream fileStream = new FileStream(Path.Combine(downloadPath, (string)_tree["info"]["name"]), FileMode.Open);
                fileStream.Seek(destinationOffset, SeekOrigin.Begin);
                byte[] _piece = new byte[pieceLength];
                fileStream.Read(_piece, 0, pieceLength);
                fileStream.Dispose();
                piece.AddRange(_piece);
            }

            return piece.ToArray();
        }

        public static string ResolvePath(string downloadDir, string relMomDir, JArray relNodes)
        {
            string filePath = Path.Combine(downloadDir, relMomDir);

            for (int i = 0; i < relNodes.Count; i++)
            {
                string pathComponent = (string)relNodes[i];
                // filePath += Path.DirectorySeparatorChar + pathComponent;
                filePath = Path.Combine(filePath, pathComponent);
            }

            return filePath;
        }

        public static void ReadDictionary(ref byte[] content, ref int position, string path, ref JObject tree)
        {
            // skip d
            position++;

            while (!isTerminator(GetASCIIChar(content, position)))
            {
                string key = ReadString(ref content, ref position, false);
                VarType varType = GetNextVarType(ref content, position);
                string subPath;

                switch (varType)
                {
                    case VarType.String:
                        if (String.IsNullOrEmpty(path))
                        {
                            tree[key] = key.Equals("pieces") ? ReadString(ref content, ref position, true) : ReadString(ref content, ref position, false);
                        }
                        else
                        {
                            tree.SelectToken(path)[key] = key.Equals("pieces") ? ReadString(ref content, ref position, true) : ReadString(ref content, ref position, false);
                        }

                        break;
                    case VarType.Integer:
                        if (String.IsNullOrEmpty(path))
                        {
                            tree[key] = ReadInteger(ref content, ref position);
                        }
                        else
                        {
                            tree.SelectToken(path)[key] = ReadInteger(ref content, ref position);
                        }

                        break;
                    case VarType.Dictionary:
                        if (String.IsNullOrEmpty(path))
                        {
                            tree[key] = new JObject();
                        }
                        else
                        {
                            tree.SelectToken(path)[key] = new JObject();
                        }

                        subPath = path + "." + key;
                        ReadDictionary(ref content, ref position, subPath, ref tree);
                        break;
                    default:
                        if (String.IsNullOrEmpty(path))
                        {
                            tree[key] = new JArray();
                        }
                        else
                        {
                            tree.SelectToken(path)[key] = new JArray();
                        }

                        subPath = path + "." + key;
                        ReadList(ref content, ref position, subPath, ref tree);
                        break;
                }
            }

            position++;
        }

        public static void ReadList(ref byte[] content, ref int position, string path, ref JObject map)
        {
            // skipping l
            position++;
            int index = 0;

            while (!isTerminator(GetASCIIChar(content, position)))
            {
                VarType varType = GetNextVarType(ref content, position);
                string subPath;

                switch (varType)
                {
                    case VarType.String:
                        (map.SelectToken(path) as JArray).Add(ReadString(ref content, ref position, false));
                        break;
                    case VarType.Integer:
                        (map.SelectToken(path) as JArray).Add(ReadInteger(ref content, ref position));
                        break;
                    case VarType.Dictionary:
                        (map.SelectToken(path) as JArray).Add(new JObject());
                        subPath = path + "[" + index + "]";
                        ReadDictionary(ref content, ref position, subPath, ref map);
                        break;
                    default:
                        (map.SelectToken(path) as JArray).Add(new JArray());
                        subPath = path + "[" + index + "]";
                        ReadList(ref content, ref position, subPath, ref map);
                        break;
                }

                index++;
            }

            position++;
        }

        public static string ReadInteger(ref byte[] content, ref int position)
        {
            // skipping i
            position++;
            char c = GetASCIIChar(content, position);
            string integer = c + "";
            position++;
            c = GetASCIIChar(content, position);

            while (Char.IsDigit(c))
            {
                integer += c;
                position++;
                c = GetASCIIChar(content, position);
            }

            // skipping e
            position++;
            return integer;
        }

        public static string ReadString(ref byte[] content, ref int position, bool asHex)
        {
            char c = GetASCIIChar(content, position);
            string length = c + "";
            position++;
            c = GetASCIIChar(content, position);

            while (Char.IsDigit(c))
            {
                length += c;
                position++;
                c = GetASCIIChar(content, position);
            }

            // Skipping colon
            position++;
            string str = "";
            int lengthInteger = Int32.Parse(length);

            if (asHex)
            {
                byte[] bin = new byte[lengthInteger];
                Array.Copy(content, position, bin, 0, bin.Length);
                str = B2S(bin);
            }
            else
            {
                str = GetASCIIStr(content, position, lengthInteger);
            }


            position += lengthInteger;
            return str;
        }

        public static string GetASCIIStr(byte[] buffer, int position, int length)
        {
            return Encoding.ASCII.GetString(buffer, position, length);
        }

        public static char GetASCIIChar(byte[] buffer, int position)
        {
            return GetASCIIStr(buffer, position, 1)[0];
        }

        public static bool isTerminator(char c)
        {
            if (c.Equals('e'))
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        public enum VarType
        {
            String,
            Integer,
            Dictionary,
            List
        }

        public static VarType GetNextVarType(ref byte[] content, int position)
        {
            char character = Encoding.UTF8.GetString(content, position, 1)[0];

            switch (character)
            {
                case 'd':
                    return VarType.Dictionary;
                case 'l':
                    return VarType.List;
                case 'i':
                    return VarType.Integer;
                default:
                    return VarType.String;
            }
        }

        public static string B2S(byte[] ba)
        {
            return BitConverter.ToString(ba).Replace("-", "");
        }
    }
}
