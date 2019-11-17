using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Terraria;
using TShockAPI;
using TerrariaApi.Server;
using Microsoft.Xna.Framework;
using System.Timers;
using MySql.Data.MySqlClient;
using OTAPI.Tile;
using System.Reflection;
using System.IO;
using System.Security.AccessControl;

namespace Tgo_WEdit
{
    [ApiVersion(2, 1)]
    public class Tgo_WEdit : TerrariaPlugin
    {
        private int point = 0;
        private CommandArgs gArgs;
        private Timer timeout;

        //MySql connection guide used: https://www.codeproject.com/Articles/43438/Connect-C-to-MySQL
        private MySqlConnection mysql;
        private string server;
        private string db;
        private string uid;
        private string pass;

        public override string Author => "Tsohg";
        public override string Name => "TGO_WEdit";
        public override string Description => "Used in conjunction with TGO_Req for TGO button commands related to World Editing.";
        public override Version Version => new Version(1, 0);

        private static Dictionary<TSPlayer, TgoTileData> clipboard;
        private static string dataDirPath;

        public Tgo_WEdit(Main game) : base(game)
        {
        }

        public override void Initialize()
        {
            try
            {
                timeout = new Timer(10000); //10 seconds
                timeout.AutoReset = false;
                timeout.Elapsed += Timeout;

                server = "sql-us-northeast.nodecraft.com";
                db = "np2_92b7e33b12fcdb170c";
                uid = "np2_060d35d64805";
                pass = "5a2645945cf7c6de9cdef46c";
                mysql = new MySqlConnection(
                    "SERVER=" + server + ";" +
                    "DATABASE=" + db + ";" +
                    "UID=" + uid + ";" +
                    "PASSWORD=" + pass + ";");

                //Register all commands with TShock to be able to pull method calls into Tgo_Requests
                Command c = new Command("TGO.Point1", Point1, "Point1", "point1", "p1");
                Commands.ChatCommands.Add(c);

                Commands.ChatCommands.Add(new Command("TGO.Point2", Point2, "Point2", "point2", "p2"));
                Commands.ChatCommands.Add(new Command("TGO.Cut", Cut, "Cut", "cut"));
                //Commands.ChatCommands.Add(new Command("TGO.Paste", Paste, "Paste", "paste"));
                Commands.ChatCommands.Add(new Command("TGO.Undo", Undo, "Undo", "undo"));
                clipboard = new Dictionary<TSPlayer, TgoTileData>(); //for paste

                string dataDirName = "TgoTileData";
                if (!Directory.Exists(dataDirName))
                    Directory.CreateDirectory(dataDirName);
                dataDirPath = Path.GetFullPath(dataDirName);
            }
            catch (Exception e)
            {
                TShock.Log.ConsoleError("TGOWEDIT Error: " + e.Message);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                GetDataHandlers.TileEdit -= GetPoint;
                timeout.Dispose();
                gArgs = null;
            }
            base.Dispose(disposing);
        }

        private TSPlayer GetTSPlayerByName(string name)
        {
            foreach (TSPlayer plr in TShock.Players)
                if (plr.Name == name)
                    return plr;
            return null;
        }

        #region Points

        //! Testing required for this region.
        private void GetPoint(object sender, GetDataHandlers.TileEditEventArgs e)
        {
            gArgs.Player.TempPoints[point] = new Point(e.X, e.Y);
            gArgs.Player.SendSuccessMessage(gArgs.Player.TempPoints[point].X + ", " + gArgs.Player.TempPoints[point].Y.ToString());
            GetDataHandlers.TileEdit -= GetPoint;
            timeout.Stop();
            //replace tile that was changed
            ITile tile = Main.tile[e.X, e.Y];
            ReplaceTile(tile, GetTileFromAction(e));
        }

        private void PointSetup(CommandArgs args)
        {
            gArgs = args;
            timeout.Start();
            GetDataHandlers.TileEdit += GetPoint;
            args.Player.SendMessage("You have 10 seconds to select point " + (point + 1) + ".", Color.SteelBlue);
        }

        public void Point1(CommandArgs args)
        {
            point = 0;
            PointSetup(args);
        }

        public void Point2(CommandArgs args)
        {
            point = 1;
            PointSetup(args);
        }

        private void Timeout(object sender, EventArgs e)
        {
            GetDataHandlers.TileEdit -= GetPoint;
            timeout.Stop();
            gArgs.Player.SendErrorMessage("Point timeout. Please reuse the command and try again.");
        }
        #endregion

        #region Edit Commands

        /// <summary>
        /// Returns false if a point is missing. Otherwise, returns true.
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        private bool PointCheck(CommandArgs args, bool isSingle)
        {
            if (args.Player.TempPoints[0].X == -1 || args.Player.TempPoints[0].Y == -1)
            {
                args.Player.SendErrorMessage("You must select a point 1.");
                return false;
            }
            if (!isSingle)
            {
                if (args.Player.TempPoints[1].X == -1 || args.Player.TempPoints[1].Y == -1)
                {
                    args.Player.SendErrorMessage("You must select a point 2.");
                    return false;
                }
            }

            return true;
        }

        public void Cut(CommandArgs args)
        {
            if (PointCheck(args, false))
            {
                Tuple<Point, Point> pts = CorrectPoints(args);
                args.Player.TempPoints[0] = pts.Item1;
                args.Player.TempPoints[1] = pts.Item2;

                //data collection. Reconstruct the tild id list *BEFORE* we act.
                TgoTileData ttd = new TgoTileData(pts.Item1, pts.Item2, TgoTileData.TgoAction.cut, DateTime.Now, args.Player);
                InsertTgoTileData(ttd, args);

                //perform command. This loop will be the standard loop we use throughout app.
                //args.Player.SendInfoMessage((args.Player.TempPoints[0].X < args.Player.TempPoints[1].X).ToString());
                for (int x = ttd.p1.X; x <= ttd.p2.X; x++)
                {
                    //args.Player.SendInfoMessage((args.Player.TempPoints[0].Y < args.Player.TempPoints[1].Y).ToString());
                    for (int y = ttd.p2.Y; y <= ttd.p1.Y; y++)
                    {
                       //TShock.Log.ConsoleInfo("" + Main.tile[x, y].ToString()); //framex and framey are both 0 for air.

                        Main.tile[x, y].active(false);
                        //Main.tile[x, y] = new Tile(); //interesting in that it obliterates background walls too.
                        TSPlayer.All.SendTileSquare(x, y);
                        args.Player.SendTileSquare(x, y);
                    }
                }

                //null out points
                args.Player.TempPoints[0] = new Point(-1, -1);
                args.Player.TempPoints[1] = new Point(-1, -1);

                args.Player.SendSuccessMessage("Area successfully cut.");
            }
            else return;
        }

        /// <summary>
        /// We have decided to utilize database instead of more network coding.
        /// Query the database, utilize the proper data file, then paste.
        /// </summary>
        /// <param name="args"></param>
        public void Paste(CommandArgs args)
        {
            if (PointCheck(args, true))
            {

            }
            else return;
        }

        public void Undo(CommandArgs args)
        {
            //query the last file name to be made by the player.
            //get player ID
            string query = "SELECT UID FROM TGO_USERS WHERE NAME = '" + args.Player.Name + "'";
            string[] colNames = { "UID" };
            List<string>[] results = ExecuteSelectQuery(query, colNames);

            if (results[0].Count <= 0) //insert new user then insert the rest of the data.
            {
                args.Player.SendErrorMessage("You must first use any other command before you can undo the command.");
                return;
            }

            //get last file by player
            query = "SELECT TIMESTAMP, FILE_NAME FROM TGO_TILEDATA WHERE UID = " + results[0][0] + " ORDER BY TIMESTAMP DESC LIMIT 1";
            colNames = new string[] { "TIMESTAMP", "FILE_NAME" };
            results = ExecuteSelectQuery(query, colNames); //results[0][1] contains the filename for latest file.

            //get file
            string filePath = dataDirPath + @"/" + results[0][1]; //FileUseOne
            byte[] file = System.IO.File.ReadAllBytes(filePath);

            //decode file contents to a list of ushort, length, and width.f
            int offset = 0;
            Tuple<Point, Point, int, int, List<ushort>> tileData = TgoTileData.DecodeData(file, ref offset);
            offset = 0;
            //TShock.Log.ConsoleInfo(tileData.Item1.X + ", " + tileData.Item1.Y + " : " + tileData.Item2.X + ", " + tileData.Item2.Y);
            //TShock.Log.ConsoleInfo(tileData.Item3 + ", " + tileData.Item4 + " : " + tileData.Item5.Count);
            for (int x = tileData.Item1.X; x <= tileData.Item2.X; x++)
            {
                //args.Player.SendInfoMessage((args.Player.TempPoints[0].Y < args.Player.TempPoints[1].Y).ToString());
                for (int y = tileData.Item2.Y; y <= tileData.Item1.Y; y++)
                {
                    Tile t = new Tile();
                    if (tileData.Item5[offset] > 0)
                    {
                        t.type = --tileData.Item5[offset];
                        t.active(true);
                        Main.tile[x, y] = t;
                        TSPlayer.All.SendTileSquare(x, y);
                        args.Player.SendTileSquare(x, y);
                    }
                    else
                    {
                        Main.tile[x, y].active(false);
                    }
                    offset++;
                }
            }
            query = "DELETE FROM TGO_TILEDATA WHERE FILE_NAME = '" + results[0][1] + "'";
            ExecuteNoResultQuery(query);
            args.Player.SendSuccessMessage("Successfully undid the last edit operation.");
            //use the loop to create new tiles at the location based on length/width.
            //start at p.X, end at p.X + length.
            //start at p.Y, end at P.Y + width.
            //offset = 0;
            //for(int x = args.Player.TempPoints[0].X; x <= args.Player.TempPoints[0].X + tileData.Item1; x++)
            //{
            //    for(int y = args.Player.TempPoints[0].Y; y <= args.Player.TempPoints[0].Y + tileData.Item2; y++)
            //    {
            //        Tile t = new Tile();
            //        t.type = tileData.Item3[offset];
            //        t.active(true);
            //        Main.tile[x, y] = t;
            //        offset++;
            //    }
            //}
        }

        /// <summary>
        /// Sets the given points to the Top Left and Bottom Right positions. (Point 1 and Point 2 respectively).
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        private Tuple<Point, Point> CorrectPoints(CommandArgs args)
        {
            //min of both X, max of both Y = top left corner of a square.
            Point cP1 = new Point(Math.Min(args.Player.TempPoints[0].X, args.Player.TempPoints[1].X), Math.Max(args.Player.TempPoints[0].Y, args.Player.TempPoints[1].Y));
            //max of both X, min of both Y = bottom right corner of a square.
            Point cP2 = new Point(Math.Max(args.Player.TempPoints[0].X, args.Player.TempPoints[1].X), Math.Min(args.Player.TempPoints[0].Y, args.Player.TempPoints[1].Y));

            return new Tuple<Point, Point>(cP1, cP2);
        }
        #endregion

        #region References MySQL Guide
        private void ExecuteNoResultQuery(string query)
        {
            try
            {
                mysql.Open();
                MySqlCommand cmd = new MySqlCommand(query, mysql);
                cmd.ExecuteNonQuery();
                mysql.Close();
            }
            catch (Exception e)
            {
                TShock.Log.ConsoleError("TGOEDIT Error on ExecuteQuery: " + e.Message);
            }
        }

        private List<string>[] ExecuteSelectQuery(string query, string[] colNames)
        {
            List<string>[] resultSet = new List<string>[colNames.Length];
            for (int i = 0; i < colNames.Length; i++)
                resultSet[i] = new List<string>();
            mysql.Open();
            MySqlCommand cmd = new MySqlCommand(query, mysql);
            MySqlDataReader reader = cmd.ExecuteReader();
            int record = 0;
            while (reader.Read())
            {
                for (int i = 0; i < colNames.Length; i++)
                {
                    resultSet[record].Add(reader[colNames[i]] + "");
                }
                record++;
            }
            reader.Close();
            mysql.Close();
            return resultSet;
        }
        #endregion

        /// <summary>
        /// Borrowed code for DB from my 498 class. I typed it in two places for modularity so one may function without a .dll dependency.
        /// </summary>
        /// <param name="data"></param>
        /// <param name="args"></param>
        private void InsertTgoTileData(TgoTileData data, CommandArgs args)
        {
            try
            {
                //first, find out if the user is already in the database. if so, we ignore the first insert, else we insert a new tgouser
                string query = "SELECT NAME FROM TGO_USERS WHERE NAME = '" + args.Player.Name + "'";
                string[] colNames = { "NAME" };
                List<string>[] results = ExecuteSelectQuery(query, colNames);

                if (results[0].Count <= 0) //insert new user then insert the rest of the data.
                {
                    query = "INSERT INTO TGO_USERS (NAME) VALUES('" + args.Player.Name + "')";
                    ExecuteNoResultQuery(query);
                }

                //get primary key after the insert.
                query = "SELECT UID, NAME FROM TGO_USERS WHERE NAME = '" + args.Player.Name + "'";
                colNames = new string[] { "UID", "NAME" };
                results = ExecuteSelectQuery(query, colNames); // results[0] => List looks like: UID, NAME, etc. should be exactly 1 result.
                int pk = int.Parse(results[0][0]); //should be UID

                query = "INSERT INTO TGO_TILEDATA (UID, EID, FILE_NAME) VALUES (" + pk + ", " + (1 + (int)data.editAction) + ", '" + data.fileName + "')";
                ExecuteNoResultQuery(query);
            }
            catch (Exception e)
            {
                TShock.Log.ConsoleError("TGOWEDIT Error: " + e.Message);
            }
        }

        #region Tile Management
        /// <summary>
        /// Replaces tile with the target.
        /// </summary>
        /// <param name="tile">Tile to be replaced.</param>
        /// <param name="target">Target tile that is replacing the tile.</param>
        public void ReplaceTile(ITile tile, ITile target)
        {
            Main.tile[tile.frameX, tile.frameY] = target;
            TSPlayer.All.SendTileSquare(tile.frameX, tile.frameY);
        }

        /// <summary>
        /// Used to return the tile that was created or destroyed.
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        public ITile GetTileFromAction(GetDataHandlers.TileEditEventArgs args)
        {
            Vector2 loc = new Vector2(args.X, args.Y);

            switch (args.Action)
            {
                case GetDataHandlers.EditAction.KillTile:

                case GetDataHandlers.EditAction.KillTileNoItem:

                case GetDataHandlers.EditAction.KillWall:

                case GetDataHandlers.EditAction.KillActuator:

                case GetDataHandlers.EditAction.KillWire:

                case GetDataHandlers.EditAction.KillWire2:

                case GetDataHandlers.EditAction.KillWire3:
                    return args.Player.TilesDestroyed[loc];

                //will return the same tile, but just turn it off so it vanishes.
                default:
                    ITile tile = args.Player.TilesCreated[loc];
                    tile.active(false);
                    return tile;
            }
        }

        /// <summary>
        /// Replaces tiles at the given positions in an array with the given tile.
        /// </summary>
        /// <param name="array">Array of tiles to be replaced.</param>
        /// <param name="tile">Target tile that will replace all tiles in array.</param>
        public void ReplaceTileArray(ITile[] array, ITile tile)
        {
            foreach (ITile t in array)
                ReplaceTile(t, tile);
        }
        #endregion

        /// <summary>
        /// Used to store the tile data associated with one action.
        /// </summary>
        public class TgoTileData
        {
            /// <summary>
            /// There are a few ways I can handle the file format. This method appears to be one simplest yet effective methods as each tileid between 2 points will only 
            /// require storing the tileid. Each tileid will occur in the same order as the nested loop's iteration.
            /// </summary>

            /* Format:
             * Point1.X as u(nsigned)leb128,
             * Point1.Y as uleb128,
             * Point2.X as uleb128,
             * Point2.Y as uleb128,
             * length as uleb128,
             * width as uleb128,
             * # of TileIds as uleb128,
             * All TileIds as uleb128
             */

            public enum TgoAction
            {
                cut,
                paste,
                undo
            }

            public string fileName;
            public Point p1;
            public Point p2;
            public TgoAction editAction;
            public DateTime time;
            public List<ushort> tileIds;

            public int length;
            public int width;

            private Queue<byte> buffer; //buffer of bytes to be written to file.
            private TSPlayer plr;
            //private List<int> tileBackround; //will have to figure out background walls later.

            public TgoTileData(Point p1, Point p2, TgoAction editAction, DateTime time, TSPlayer plr)
            {
                this.p1 = p1;
                this.p2 = p2;
                this.editAction = editAction;
                this.time = time;
                tileIds = new List<ushort>();
                fileName = "TileEditData_" + time.ToString().Replace('/', '-').Replace(' ', '-');
                //These will always be positive due to CorrectPoints.
                length = Math.Abs(p1.X - p2.X);
                width = Math.Abs(p1.Y - p2.Y);
                buffer = new Queue<byte>();
                this.plr = plr;

                for (int x = p1.X; x <= p2.X; x++)
                {
                    for (int y = p2.Y; y <= p1.Y; y++)
                    {
                        //if framex and framey are 0, air block so we do not increment by 1.
                        //all other tiles get an increment of 1 and must be subtracted from later.
                        if (Main.tile[x, y].frameX == 0 & Main.tile[x, y].frameY == 0 && !Main.tile[x, y].active())
                        {
                            tileIds.Add(Main.tile[x, y].type);
                        }
                        else
                        {
                            tileIds.Add(++Main.tile[x, y].type); //other tiles increment by 1.
                        }
                        //if ((Main.tile[x, y].frameX == -1 || Main.tile[x, y].frameY == -1))
                        //{
                        //    ushort newType = Main.tile[x, y].type;
                        //    newType++;
                        //    tileIds.Add(newType);
                        //}
                        //else
                        //{
                        //    tileIds.Add(Main.tile[x, y].type); //air blocks are now 0.
                        //}
                    }
                }

                EncodeData();
                if (clipboard.ContainsKey(plr))
                    clipboard[plr] = this;
                else
                    clipboard.Add(plr, this);
                WriteData();
                //TestRead();
            }

            /// <summary>
            /// Fill in the associated data from each tile. Then write it to a file.
            /// </summary>
            private void EncodeData()
            {
                //file header 4 bytes: T T 1 0
                buffer.Enqueue(84);
                buffer.Enqueue(84);
                buffer.Enqueue(1); //version of encoding
                buffer.Enqueue(0); //format type of encoding

                //tile data header: original x1, original y1, original x2, original y2, length, width, # of tileIDs, TileIds to #
                BufferUleb((uint)p1.X, ref buffer);
                BufferUleb((uint)p1.Y, ref buffer);
                BufferUleb((uint)p2.X, ref buffer);
                BufferUleb((uint)p2.Y, ref buffer);
                BufferUleb((uint)length, ref buffer);
                BufferUleb((uint)width, ref buffer);
                BufferUleb((uint)tileIds.Count, ref buffer); //count of tileIds.

                //Input all tile data using the standard loop.
                for (int i = 0; i < tileIds.Count; i++)
                    BufferUleb(tileIds[i], ref buffer);

                //file footer: 0x00 for termination
                buffer.Enqueue(0);
                //TShock.Log.ConsoleInfo("Before Encoding: " + p1.X + ", " + p1.Y + ", " + p2.X + ", " + p2.Y + ", " + length + ", " + width + ", " + tileIds.Count);
            }

            private void WriteData()
            {
                //Directory.GetAccessControl(dataDirPath).AddAccessRule(new FileSystemAccessRule("Full", FileSystemRights.FullControl, AccessControlType.Allow));
                //TShock.Log.ConsoleError(dataDirPath);
                string file = dataDirPath + @"/" + fileName; //FileUseTwo
                //TShock.Log.ConsoleError(file);
                System.IO.File.WriteAllBytes(file, buffer.ToArray());
            }

            public static Tuple<Point, Point, int, int, List<ushort>> DecodeData(byte[] data, ref int offset)
            {
                Point p1;
                Point p2;
                int length;
                int width;
                int count;
                List<ushort> tiles = new List<ushort>();

                //skip 4 bytes for magic values
                offset = 4;
                p1 = new Point(ConsumeUleb(data, ref offset), ConsumeUleb(data, ref offset));
                p2 = new Point(ConsumeUleb(data, ref offset), ConsumeUleb(data, ref offset));
                length = ConsumeUleb(data, ref offset);
                width = ConsumeUleb(data, ref offset);
                count = ConsumeUleb(data, ref offset);

                for (int x = p1.X; x <= p2.X; x++)
                {
                    for (int y = p2.Y; y <= p1.Y; y++)
                    {
                        tiles.Add(Main.tile[x, y].type); //air blocks = 0
                    }
                }

                return new Tuple<Point, Point, int, int, List<ushort>>(p1, p2, length, width, tiles);
                //return "After Decoding: " + p1.X + ", " + p1.Y + ", " + p2.X + ", " + p2.Y + ", " + length + ", " + width + ", " + area;
            }

            /// <summary>
            /// Modified DiLemming code for reading a uleb.
            /// Taken from my Luajit Decompiler
            /// </summary>
            /// <param name="bytes"></param>
            /// <param name="offset"></param>
            /// <returns></returns>
            private static int ConsumeUleb(byte[] bytes, ref int offset)
            {
                int count = 0;
                int shift = 1;
                int cont = 0;
                byte b;
                int data;
                int value = 0;
                do
                {
                    b = bytes[offset + count];
                    data = b & 127;
                    cont = b & 128;
                    value += data * shift;
                    shift *= 128;
                    count++;
                } while (cont != 0);
                offset += count;
                return value;
            }

            /// <summary>
            /// Psuedo-code sourced from: https://en.wikipedia.org/wiki/LEB128#Encode_unsigned_integer
            /// </summary>
            /// <param name="value"></param>
            public void BufferUleb(uint value, ref Queue<byte> buffer)
            {
                do
                {
                    byte low7 = (byte)(value & 127); //get 7 lowest bits
                    value >>= 7; //erase lowest 7 bits from value
                    if (value != 0) //more bytes so set 8th bit to 1.
                        low7 |= 128;
                    buffer.Enqueue(low7);
                } while (value != 0);
            }
        }
    }
}
