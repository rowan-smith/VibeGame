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
                ui.DrawRectangle(startX + i * (slotSize + spacing), startY, slotSize, slotSize, new Vector4(0.2f, 0.2f, 0.2f, 1.0f));
                if (i == selectedSlot)
                    ui.DrawRectangleLines(startX + i * (slotSize + spacing), startY, slotSize, slotSize, new Vector4(1, 1, 0, 1));
                var item = _items.GetItemInSlot(i);
                if (item != null)
                {
                    // Draw item texture in slot (placeholder)
                }
            }
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
