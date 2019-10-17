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

namespace Tgo_WEdit
{
    class Tgo_WEdit : TerrariaPlugin
    {
        private int point = 0;
        private CommandArgs args;
        private Timer timeout;

        public Tgo_WEdit(Main game) : base(game)
        {
        }

        public override void Initialize()
        {
            timeout = new Timer(10000); //10 seconds
            timeout.AutoReset = false;
            timeout.Elapsed += Timeout;
        }

        protected override void Dispose(bool disposing)
        {
            if(disposing)
            {
                GetDataHandlers.TileEdit -= GetPoint;
                timeout.Dispose();
                args = null;
            }
            base.Dispose(disposing);
        }

        #region Points

        //! Testing required for this region.
        private void GetPoint(object sender, GetDataHandlers.TileEditEventArgs e)
        {
            args.Player.TempPoints[point] = new Point(e.X, e.Y);
            GetDataHandlers.TileEdit -= GetPoint;
            timeout.Stop();
        }

        private void PointSetup(CommandArgs args)
        {
            this.args = args;
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
            if(args.Player.TempPoints[0] == null)
            {
                args.Player.SendErrorMessage("You must select a point 1.");
                return false;
            }

            if(args.Player.TempPoints[1] == null)
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
                //min of both X, max of both Y = top left corner of a square.
                Point cP1 = new Point(Math.Min(args.Player.TempPoints[0].X, args.Player.TempPoints[1].X), Math.Max(args.Player.TempPoints[0].Y, args.Player.TempPoints[1].Y));
                //max of both X, min of both Y = bottom right corner of a square.
                Point cP2 = new Point(Math.Max(args.Player.TempPoints[0].X, args.Player.TempPoints[1].X), Math.Min(args.Player.TempPoints[0].Y, args.Player.TempPoints[1].Y));

                //data collection
                TgoTileData ttd = new TgoTileData(cP1, cP2, TgoTileData.TgoAction.cut, DateTime.Now);
                //ttd.Encode(PATH HERE); //encode tile data into the file format and save in the given path with a given name.
            }
            else return;
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
                cut
            }

            private Point p1;
            private Point p2;
            private TgoAction editAction;
            private DateTime time;
            private List<ushort> tileIds;
            //private List<int> tileBackround; //will have to figure out background walls later.

            public TgoTileData(Point p1, Point p2, TgoAction editAction, DateTime time)
            {
                this.p1 = p1;
                this.p2 = p2;
                this.editAction = editAction;
                this.time = time;
                GetData();
            }

            /// <summary>
            /// Fill in the associated data from each tile.
            /// </summary>
            private void GetData()
            {

            }

            public void Encode(string path)
            {

            }
        }
    }
}
