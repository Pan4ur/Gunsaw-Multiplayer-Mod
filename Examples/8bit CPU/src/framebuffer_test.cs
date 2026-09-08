public static class FramebufferTest
{
    public static void Main()
    {
        byte x = 0;
        byte y = 0;
        byte i = 0;
        byte p = 0;
        bool expected = false;
        bool actual = false;
        bool ok = true;

        Vram0.ClearAll();
        Vram1.ClearAll();

        // walking through every P0 cell
        i = 0;
        p = Coord.Make(0, 0);
        Vram0.Set(p);
        while (i < 63)
        {
            Cpu.Wait(1);
            Vram0.Clear(p);
            i++;
            p = Coord.Make(i & 7, i >> 3);
            Vram0.Set(p);
        }
        Cpu.Wait(1);
        Vram0.Clear(p);

        // horizontal lines
        y = 0;
        while (y < 8)
        {
            x = 0;
            while (x < 8)
            {
                Vram0.Set(Coord.Make(x, y));
                x++;
            }
            Cpu.Wait(1);
            x = 0;
            while (x < 8)
            {
                Vram0.Clear(Coord.Make(x, y));
                x++;
            }
            y++;
        }

        // vertical lines
        x = 0;
        while (x < 8)
        {
            y = 0;
            while (y < 8)
            {
                Vram0.Set(Coord.Make(x, y));
                y++;
            }
            Cpu.Wait(1);
            y = 0;
            while (y < 8)
            {
                Vram0.Clear(Coord.Make(x, y));
                y++;
            }
            x++;
        }

        // x
        i = 0;
        while (i < 8)
        {
            Vram0.Set(Coord.Make(i, i));
            Vram0.Set(Coord.Make(7 - i, i));
            i++;
        }
        Cpu.Wait(3);
        Vram0.ClearAll();

        // checkerboard with confirm (P0)
        y = 0;
        while (y < 8)
        {
            x = 0;
            while (x < 8)
            {
                p = Coord.Make(x, y);
                if (((x ^ y) & 1) == 0)
                    Vram0.Set(p);
                x++;
            }
            y++;
        }
        Cpu.Wait(2);

        y = 0;
        while (y < 8 && ok)
        {
            x = 0;
            while (x < 8 && ok)
            {
                p = Coord.Make(x, y);
                expected = ((x ^ y) & 1) == 0;
                actual = Vram0.Read(p);
                if (actual != expected)
                    ok = false;
                x++;
            }
            y++;
        }

        // border
        Vram0.ClearAll();
        i = 0;
        while (i < 8)
        {
            Vram0.Set(Coord.Make(i, 0));
            Vram0.Set(Coord.Make(i, 7));
            Vram0.Set(Coord.Make(0, i));
            Vram0.Set(Coord.Make(7, i));
            i++;
        }
        Cpu.Wait(3);

        // :)
        Vram0.ClearAll();
        Vram0.Set(Coord.Make(2, 2));
        Vram0.Set(Coord.Make(5, 2));
        Vram0.Set(Coord.Make(1, 4));
        Vram0.Set(Coord.Make(6, 4));
        Vram0.Set(Coord.Make(2, 5));
        Vram0.Set(Coord.Make(3, 6));
        Vram0.Set(Coord.Make(4, 6));
        Vram0.Set(Coord.Make(5, 5));
        Cpu.Wait(3);

        // fill & clear
        y = 0;
        while (y < 8)
        {
            x = 0;
            while (x < 8)
            {
                Vram0.Set(Coord.Make(x, y));
                x++;
            }
            y++;
        }
        y = 0;
        while (y < 8)
        {
            x = 0;
            while (x < 8)
            {
                Vram0.Clear(Coord.Make(x, y));
                x++;
            }
            y++;
        }

        // checkerboard with confirm (P1)
        y = 0;
        while (y < 8)
        {
            x = 0;
            while (x < 8)
            {
                p = Coord.Make(x, y);
                if (((x ^ y) & 1) != 0)
                    Vram1.Set(p);
                x++;
            }
            y++;
        }

        y = 0;
        while (y < 8 && ok)
        {
            x = 0;
            while (x < 8 && ok)
            {
                p = Coord.Make(x, y);
                expected = ((x ^ y) & 1) != 0;
                actual = Vram1.Read(p);
                if (actual != expected)
                    ok = false;
                x++;
            }
            y++;
        }

        // border (P0) + X (P1)
        Vram0.ClearAll();
        Vram1.ClearAll();
        i = 0;
        while (i < 8)
        {
            Vram0.Set(Coord.Make(i, 0));
            Vram0.Set(Coord.Make(i, 7));
            Vram0.Set(Coord.Make(0, i));
            Vram0.Set(Coord.Make(7, i));
            Vram1.Set(Coord.Make(i, i));
            Vram1.Set(Coord.Make(7 - i, i));
            i++;
        }
        Cpu.Wait(4);

        Vram0.ClearAll();
        Vram1.ClearAll();

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
            i = 0;
            while (i < 8)
            {
                Vram0.Set(Coord.Make(i, i));
                Vram0.Set(Coord.Make(7 - i, i));
                i++;
            }
        }

        Cpu.Halt();
    }
}
