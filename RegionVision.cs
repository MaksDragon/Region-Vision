using System;
using System.Collections.Generic;
using System.Timers;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures; // Необходим для BitsByte, если возникнет конфликт типов
using Terraria.ID;
using TerrariaApi.Server;
using TShockAPI;

namespace RegionVision
{
    [ApiVersion(2, 1)]
    public class RegionVision : TerrariaPlugin
    {
        public override string Name => "RegionVision";
        public override string Author => "yomissayy";
        public override string Description => "Shows TShock region borders visually with actuator effect";
        public override Version Version => new Version(1, 1, 1);

        private static System.Timers.Timer updateTimer;
        public static HashSet<int> EnabledPlayers = new();

        public RegionVision(Main game) : base(game)
        {
        }

        public override void Initialize()
        {
            ServerApi.Hooks.GameInitialize.Register(this, OnInitialize);
            ServerApi.Hooks.ServerLeave.Register(this, OnLeave);

            updateTimer = new System.Timers.Timer(1500);
            updateTimer.Elapsed += UpdateRegions;
            updateTimer.Start();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ServerApi.Hooks.GameInitialize.Deregister(this, OnInitialize);
                ServerApi.Hooks.ServerLeave.Deregister(this, OnLeave);

                if (updateTimer != null)
                {
                    updateTimer.Stop();
                    updateTimer.Dispose();
                }
            }
            base.Dispose(disposing);
        }

        private void OnInitialize(EventArgs args)
		{
			// Вместо пустой строки или сторонних проверок, жестко прописываем пермишен:
			Commands.ChatCommands.Add(new Command("regionvision.use", CmdRegionVision, "rv"));
		}

        private void OnLeave(LeaveEventArgs args)
        {
            TSPlayer player = TShock.Players[args.Who];
            bool wasEnabled = false;
    
            lock (EnabledPlayers)
            {
                if (EnabledPlayers.Contains(args.Who))
                {
                    EnabledPlayers.Remove(args.Who);
                    wasEnabled = true;
                }
            }

            if (wasEnabled && player != null)
            {
                // Защита от изменения коллекции регионов TShock из другого потока
                List<TShockAPI.DB.Region> regionsSnapshot;
                lock (TShock.Regions.Regions)
                {
                    regionsSnapshot = new List<TShockAPI.DB.Region>(TShock.Regions.Regions);
                }

                foreach (var region in regionsSnapshot)
                {
                    Rectangle rect = new Rectangle(
                        region.Area.Left,
                        region.Area.Top,
                        region.Area.Width,
                        region.Area.Height
                    );
                    RestoreRectangle(player, rect);
                }
            }
        }

        private void CmdRegionVision(CommandArgs args)
        {
            TSPlayer player = args.Player;
            if (player == null || !player.Active)
                return;

            bool isEnabled;
            lock (EnabledPlayers)
            {
                if (EnabledPlayers.Contains(player.Index))
                {
                    EnabledPlayers.Remove(player.Index);
                    isEnabled = false;
                }
                else
                {
                    EnabledPlayers.Add(player.Index);
                    isEnabled = true;
                }
            }

            // Защита от изменения коллекции регионов TShock
            List<TShockAPI.DB.Region> regionsSnapshot;
            lock (TShock.Regions.Regions)
            {
                regionsSnapshot = new List<TShockAPI.DB.Region>(TShock.Regions.Regions);
            }

            if (!isEnabled)
            {
                foreach (var region in regionsSnapshot)
                {
                    Rectangle rect = new Rectangle(
                        region.Area.Left,
                        region.Area.Top,
                        region.Area.Width,
                        region.Area.Height
                    );
                    RestoreRectangle(player, rect);
                }
                player.SendInfoMessage("Визуализация регионов отключена.");
            }
            else
            {
                TriggerRenderForPlayer(player, regionsSnapshot);
                player.SendSuccessMessage("Визуализация регионов включена. Введите /rv повторно для отключения.");
            }
        }

        private void UpdateRegions(object sender, ElapsedEventArgs e)
        {
            int[] currentPlayers;
            lock (EnabledPlayers)
            {
                currentPlayers = new int[EnabledPlayers.Count];
                EnabledPlayers.CopyTo(currentPlayers);
            }

            if (currentPlayers.Length == 0)
                return;

            List<TShockAPI.DB.Region> regionsSnapshot;
            lock (TShock.Regions.Regions)
            {
                regionsSnapshot = new List<TShockAPI.DB.Region>(TShock.Regions.Regions);
            }

            foreach (int playerIndex in currentPlayers)
            {
                if (playerIndex < 0 || playerIndex >= Main.maxPlayers)
                    continue;

                TSPlayer player = TShock.Players[playerIndex];
                if (player == null || !player.Active || !player.ConnectionAlive)
                    continue;

                TriggerRenderForPlayer(player, regionsSnapshot);
            }
        }

        private void TriggerRenderForPlayer(TSPlayer player, List<TShockAPI.DB.Region> regions)
        {
            foreach (var region in regions)
            {
                // Не шлем пакеты, если регион находится далеко за экраном игрока
                if (Math.Abs(player.TileX - region.Area.Left) > 150 && Math.Abs(player.TileX - region.Area.Right) > 150)
                    continue;

                Rectangle rect = new Rectangle(
                    region.Area.Left,
                    region.Area.Top,
                    region.Area.Width,
                    region.Area.Height
                );

                DrawRectangle(player, rect);
            }
        }

        private void DrawRectangle(TSPlayer player, Rectangle rect)
        {
            // Шаг += 3 превращает сплошную линию в пунктир, спасая сервер от лагов
            for (int x = rect.Left; x <= rect.Right; x += 3)
            {
                SendActuatedTile(player, x, rect.Top);
                SendActuatedTile(player, x, rect.Bottom);
            }

            for (int y = rect.Top; y <= rect.Bottom; y += 3)
            {
                SendActuatedTile(player, rect.Left, y);
                SendActuatedTile(player, rect.Right, y);
            }
        }

        private void RestoreRectangle(TSPlayer player, Rectangle rect)
        {
            // Шаг очистки должен строго совпадать с шагом отрисовки
            for (int x = rect.Left; x <= rect.Right; x += 3)
            {
                ResetClientTile(player, x, rect.Top);
                ResetClientTile(player, x, rect.Bottom);
            }
            for (int y = rect.Top; y <= rect.Bottom; y += 3)
            {
                ResetClientTile(player, rect.Left, y);
                ResetClientTile(player, rect.Right, y);
            }
        }
		
        private void ResetClientTile(TSPlayer player, int x, int y)
        {
            if (x < 0 || y < 0 || x >= Main.maxTilesX || y >= Main.maxTilesY)
                return;

            if (Main.tile[x, y].active())
            {
                NetMessage.SendTileSquare(player.Index, x, y, 1);
            }
            else
            {
                NetMessage.SendData(
                    (int)PacketTypes.Tile,
                    player.Index,
                    -1,
                    null,
                    0, // Стереть блок на клиенте (KillTile)
                    x,
                    y,
                    0,
                    0
                );
            }
        }

        private void SendActuatedTile(TSPlayer player, int x, int y)
        {
            if (x < 0 || y < 0 || x >= Main.maxTilesX || y >= Main.maxTilesY)
                return;

            Tile fakeTile = new Tile();
            fakeTile.type = TileID.Glass; 
            fakeTile.active(true);
            fakeTile.inActive(true); // Проходимость актуатора
            fakeTile.color(27);      // Краска Teal

            player.SendTileSquareAndVisuals(x, y, fakeTile);
        }
    }

    public static class TSPlayerExtensions
    {
        public static void SendTileSquareAndVisuals(this TSPlayer player, int x, int y, Tile fakeTile)
        {
            // 1. Получаем интерфейс оригинального тайла из массива мира сервера
            ITile tile = Main.tile[x, y];

            // 2. Сохраняем исходное состояние, чтобы вернуть его после отправки пакета
            bool originalActive = tile.active();
            ushort originalType = tile.type;
            bool originalInActive = tile.inActive();
            byte originalColor = tile.color();

            // 3. Временно накладываем маску нашего фейкового стеклянного блока прямо на существующий тайл
            tile.active(fakeTile.active());
            tile.type = fakeTile.type;
            tile.inActive(fakeTile.inActive()); // Эффект актуатора
            tile.color(fakeTile.color());

            // 4. Отправляем стандартный пакет обновления тайла 1х1 конкретному игроку.
            NetMessage.SendTileSquare(player.Index, x, y, 1);

            // 5. Мгновенно возвращаем все оригинальные свойства тайла на сервере назад
            tile.active(originalActive);
            tile.type = originalType;
            tile.inActive(originalInActive);
            tile.color(originalColor);
        }
    }
}
