using System.Numerics;
using Veilborne.Interfaces;

namespace Veilborne.UI
{
    public sealed class HudUiController
    {
        private readonly IItemRegistry _items;

        public HudUiController(IItemRegistry items)
        {
            _items = items;
        }

        public void DrawHotbar(IUiProvider ui, int screenWidth, int screenHeight, int selectedSlot)
        {
            int slotSize = 60;
            int spacing = 5;
            int totalWidth = (slotSize * 9) + (spacing * 8);
            int startX = screenWidth / 2 - totalWidth / 2;
            int startY = screenHeight - slotSize - 10;

            for (int i = 0; i < 9; i++)
            {
                int slotX = startX + i * (slotSize + spacing);
                ui.DrawRectangle(slotX, startY, slotSize, slotSize, new Vector4(0.2f, 0.2f, 0.2f, 1.0f));
                if (i == selectedSlot)
                    ui.DrawRectangleLines(slotX, startY, slotSize, slotSize, new Vector4(1, 1, 0, 1));

                ui.DrawText((i + 1).ToString(), slotX + 4, startY + slotSize - 18, 14, new Vector4(0.75f, 0.75f, 0.75f, 1f));

                var item = _items.GetItemInSlot(i);
                if (item != null)
                {
                    string? iconKey = ResolveIconTextureKey(item.IconPath);
                    if (iconKey is not null && ui.HasTexture(iconKey))
                    {
                        int iconPad = 8;
                        ui.DrawTexture(iconKey, slotX + iconPad, startY + iconPad, 0.35f, Vector4.One);
                    }
                    else if (!string.IsNullOrWhiteSpace(item.Name))
                    {
                        int nameSize = 11;
                        int nameW = ui.MeasureText(item.Name, nameSize);
                        int nameX = slotX + (slotSize - nameW) / 2;
                        ui.DrawText(item.Name, nameX, startY + 6, nameSize, new Vector4(0.95f, 0.95f, 0.95f, 1f));
                    }
                }
            }
        }

        private static string? ResolveIconTextureKey(string iconPath)
        {
            if (string.IsNullOrWhiteSpace(iconPath))
                return null;

            string norm = iconPath.Replace('\\', '/').ToLowerInvariant();
            if (norm.Contains("shovel"))
                return "shovel_icon";
            return null;
        }

        public void DrawCrosshair(IUiProvider ui, int screenWidth, int screenHeight, bool isHit)
        {
            Vector4 color = isHit ? new Vector4(0f, 1f, 0f, 1f) : new Vector4(0.9f, 0.9f, 0.9f, 1.0f);
            int cx = screenWidth / 2;
            int cy = screenHeight / 2;
            int size = 6;
            ui.DrawLine(cx - size, cy, cx + size, cy, color);
            ui.DrawLine(cx, cy - size, cx, cy + size, color);
        }
    }
}
