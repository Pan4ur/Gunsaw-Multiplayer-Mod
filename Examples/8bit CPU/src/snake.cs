public static class Snake
{
    public static void Main()
    {
        while (true)
        {
            byte head = 0x33;
            byte food = 0;
            byte direction = 0;
            byte next = 0;
            byte tailPtr = 0;
            byte headPtr = 3;
            byte tail = 0;
            bool occupied = false;

            Vram0.ClearAll();
            Ram.Write(0, 0x30);
            Ram.Write(1, 0x31);
            Ram.Write(2, 0x32);
            Ram.Write(3, 0x33);
            Vram0.Set(0x30);
            Vram0.Set(0x31);
            Vram0.Set(0x32);
            Vram0.Set(0x33);

            food = (byte)(Random.Byte & 0x77);
            occupied = Vram0.Read(food);
            while (occupied)
            {
                food = (byte)(Random.Byte & 0x77);
                occupied = Vram0.Read(food);
            }
            Vram0.Set(food);

            while (true)
            {
                if ((InputKeys.Direction ^ direction) != 2)
                    direction = InputKeys.Direction;

                next = head;
                if (direction == 0)
                {
                    if ((head & 7) != 7)
                        next = (byte)(head + 1);
                }
                else if (direction == 1)
                {
                    if ((head & 0x70) != 0x70)
                        next = (byte)(head + 0x10);
                }
                else if (direction == 2)
                {
                    if ((head & 7) != 0)
                        next = (byte)(head - 1);
                }
                else
                {
                    if ((head & 0x70) != 0)
                        next = (byte)(head - 0x10);
                }

                if (next == head)
                    break;

                if (next == food)
                {
                    headPtr = (byte)((headPtr + 1) & 31);
                    Ram.Write(headPtr, next);
                    head = next;
                    Vram0.Set(head);

                    food = (byte)(Random.Byte & 0x77);
                    occupied = Vram0.Read(food);
                    while (occupied)
                    {
                        food = (byte)(Random.Byte & 0x77);
                        occupied = Vram0.Read(food);
                    }
                    Vram0.Set(food);
                }
                else
                {
                    tail = Ram.Read(tailPtr);
                    Vram0.Clear(tail);
                    tailPtr = (byte)((tailPtr + 1) & 31);

                    occupied = Vram0.Read(next);
                    if (occupied && next != tail)
                        break;

                    headPtr = (byte)((headPtr + 1) & 31);
                    Ram.Write(headPtr, next);
                    head = next;
                    Vram0.Set(head);
                }
            }
        }
    }
}
