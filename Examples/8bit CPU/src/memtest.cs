public static class Memtest
{
    public static void Main()
    {
        byte address = 0;
        byte pattern = 0;
        byte actual = 0;
        bool ok = true;

        Vram0.ClearAll();

        address = 0;
        while (address < 32)
        {
            pattern = (byte)(address ^ 0xA5);
            Ram.Write(address, pattern);
            address++;
        }

        address = 0;
        while (address < 32 && ok)
        {
            pattern = (byte)(address ^ 0xA5);
            actual = Ram.Read(address);
            if (actual != pattern)
                ok = false;
            address++;
        }

        address = 0;
        while (address < 32 && ok)
        {
            pattern = (byte)~(address ^ 0xA5);
            Ram.Write(address, pattern);
            address++;
        }

        address = 0;
        while (address < 32 && ok)
        {
            pattern = (byte)~(address ^ 0xA5);
            actual = Ram.Read(address);
            if (actual != pattern)
                ok = false;
            address++;
        }

        Vram0.ClearAll();
        if (ok)
        {
            Vram0.Set(Coord.Make(7, 1));
            Vram0.Set(Coord.Make(6, 2));
            Vram0.Set(Coord.Make(5, 3));
            Vram0.Set(Coord.Make(4, 4));
            Vram0.Set(Coord.Make(3, 5));
            Vram0.Set(Coord.Make(2, 6));
            Vram0.Set(Coord.Make(1, 5));
            Vram0.Set(Coord.Make(0, 4));
        }
        else
        {
            address = 0;
            while (address < 8)
            {
                Vram0.Set(Coord.Make(address, address));
                Vram0.Set(Coord.Make(7 - address, address));
                address++;
            }
        }

        Cpu.Halt();
    }
}
