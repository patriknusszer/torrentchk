using System;
using System.IO;
using Newtonsoft.Json.Linq;
using Nusstudios.Core.Console;
using Nusstudios.Parsers;

namespace Nusstudios
{
    class torrentchk
    {
        public static void Main(string[] args)
        {
            ConsoleManager cmgr = new ConsoleManager(Console.CursorLeft, Console.CursorTop, Console.BufferWidth, Console.BufferWidth);
            Out o = new Out(cmgr);
            In i = new In(cmgr);
            o.WriteLine("torrentchk v2.2");
            o.WriteLine("Note: pieces is always SHA-1 calculated");
            o.Write("Enter path to torrent file: ");
            string loc;
            i.ReadLine(out loc);
            byte[] content = File.ReadAllBytes(loc);
            o.Write("Enter download path: ");
            string dlLoc;
            i.ReadLine(out dlLoc);

            bool ok = TorrentParser.CheckTorrentIntegrityV2(loc, dlLoc, (JObject status) => {
                switch ((string)status["status"])
                {
                    case "parsing":
                        o.WriteLine("Parsing torrent file...");
                        cmgr.UpdateNextWrite();
                        break;
                    case "processing_piece":
                        int currentPiece = (int)status["processing_piece"]["current_piece"];
                        int totalPieces = (int)status["processing_piece"]["total_pieces"];
                        o.WriteLine("Processing piece " + currentPiece + " of " + totalPieces + ". All processed pieces so far are OK.");
                        break;
                }
            });

            if (ok)
            {
                o.WriteLine("Every piece passed the hash checking.");
            }
            else
            {
                o.WriteLine("Last piece did NOT pass the hash checking.");
            }

            cmgr.DisableUpdate();

            o.WriteLine("Press any key to exit...");
            string key;
            i.ReadKey(out key);
        }
    }
}
