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
                //Commands.ChatCommands.Add(new Command("TGO.Undo", Undo, "Undo", "undo"));
                //TShock.Log.ConsoleInfo("Commands loaded. Example: " + c.Name);
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
        }
        #endregion

        #region Edit Commands

        /// <summary>
        /// Returns false if a point is missing. Otherwise, returns true.
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        private bool PointCheck(CommandArgs args)
        {
            if (args.Player.TempPoints[0].X == -1 || args.Player.TempPoints[0].Y == -1)
            {
                args.Player.SendErrorMessage("You must select a point 1.");
                return false;
            }

            if (args.Player.TempPoints[1].X == -1 || args.Player.TempPoints[1].Y == -1)
            {
                args.Player.SendErrorMessage("You must select a point 2.");
                return false;
            }

            return true;
        }

        public void Cut(CommandArgs args)
        {
            if (PointCheck(args))
            {
                Tuple<Point, Point> pts = CorrectPoints(args);
                args.Player.TempPoints[0] = pts.Item1;
                args.Player.TempPoints[1] = pts.Item2;

                //data collection. Reconstruct the tild id list *BEFORE* we act.
                TgoTileData ttd = new TgoTileData(pts.Item1, pts.Item2, TgoTileData.TgoAction.cut, DateTime.Now);
                InsertTgoTileData(ttd, args); //!! Bugged at the moment.

                //perform command. This loop will be the standard loop we use throughout app.
                //args.Player.SendInfoMessage((args.Player.TempPoints[0].X < args.Player.TempPoints[1].X).ToString());
                for (int x = ttd.p1.X; x <= ttd.p2.X; x++)
                {
                    //args.Player.SendInfoMessage((args.Player.TempPoints[0].Y < args.Player.TempPoints[1].Y).ToString());
                    for (int y = ttd.p2.Y; y <= ttd.p1.Y; y++)
                    {
                        Main.tile[x, y].active(false);
                        TSPlayer.All.SendTileSquare(x, y);
                        args.Player.SendTileSquare(x, y);
                    }
                }

                //null out points
                args.Player.TempPoints[0] = new Point(-1, -1);
                args.Player.TempPoints[1] = new Point(-1, -1);

                args.Player.SendSuccessMessage("Area successfully cut.");
            }
            else args.Player.SendErrorMessage("You have not set both points.");
        }

        /// <summary>
        /// We have decided to utilize database instead of more network coding.
        /// Query the database, utilize the proper data file, then paste.
        /// </summary>
        /// <param name="args"></param>
        public void Paste(CommandArgs args)
        {
            if (PointCheck(args))
            {

            }
            else args.Player.SendErrorMessage("You have not set both points.");
        }

        public void Undo(CommandArgs args)
        {
            if (PointCheck(args))
            {

            }
            else args.Player.SendErrorMessage("You have not set both points.");
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
        private class TgoTileData
        {
            /// <summary>
            /// There are a few ways I can handle the file format. This method appears to be one simplest yet effective methods as each tileid between 2 points will only 
            /// require storing the tileid. Each tileid will occur in the same order as the nested loop's iteration.
            /// </summary>

            /* Format:
             * Point1.X as s(igned)leb128,
             * Point1.Y as sleb128,
             * Point2.X as sleb128,
             * Point2.Y as sleb128
             * EditAction as byte,
             * # of TileIds as u(nsigned)leb128,
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
            //private List<int> tileBackround; //will have to figure out background walls later.

            public TgoTileData(Point p1, Point p2, TgoAction editAction, DateTime time)
            {
                this.p1 = p1;
                this.p2 = p2;
                this.editAction = editAction;
                this.time = time;
                tileIds = new List<ushort>();
                fileName = "TileEditData"+time.ToString().Replace('/', '-').Replace(' ', '-'); //Currently having issue with this.
                //These will always be positive due to CorrectPoints.
                length = p1.X - p2.X;
                width = p1.Y - p2.Y;
                WriteData();
            }

            /// <summary>
            /// Fill in the associated data from each tile. Then write it to a file.
            /// </summary>
            private void WriteData()
            {

            }
        }
    }
}
