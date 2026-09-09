public static class Bitmask
{
    public static void Main()
    {
        /*
          it will take up 8 register cells, 1 bool = 1 byte
     
          of course, the compiler now optimizes the locals, 
          but let's imagine that they were used somewhere else later on
  
          bool a = true;
          bool b = false;
          bool c = true;
          bool d = true;
          bool e = false;
          bool f = false;
          bool g = true;
          bool h = false;
  
          if (a) Vram0.Set(Coord.Make(0,0));
          if (b) Vram0.Set(Coord.Make(1,0));
          if (c) Vram0.Set(Coord.Make(2,0));
          if (d) Vram0.Set(Coord.Make(3,0));
          if (e) Vram0.Set(Coord.Make(4,0));
          if (f) Vram0.Set(Coord.Make(5,0));
          if (g) Vram0.Set(Coord.Make(6,0));
          if (h) Vram0.Set(Coord.Make(7,0));
        */

        // and here's a "secret" method for packing 8 bools into 1 byte using bit masks

        bool i = true;
        bool j = false;
        bool k = true;
        bool l = true;
        bool m = false;
        bool n = false;
        bool o = true;
        bool p = false;
        
        byte packed = 0; // will take up only 1 register cell

        if (i) packed = packed | (1 << 0);
        if (j) packed = packed | (1 << 1);
        if (k) packed = packed | (1 << 2);
        if (l) packed = packed | (1 << 3);
        if (m) packed = packed | (1 << 4);
        if (n) packed = packed | (1 << 5);
        if (o) packed = packed | (1 << 6);
        if (p) packed = packed | (1 << 7);
        
        bool q = (packed & (1 << 0)) != 0;
        bool r = (packed & (1 << 1)) != 0;
        bool s = (packed & (1 << 2)) != 0;
        bool t = (packed & (1 << 3)) != 0;
        bool u = (packed & (1 << 4)) != 0;
        bool v = (packed & (1 << 5)) != 0;
        bool w = (packed & (1 << 6)) != 0;
        bool x = (packed & (1 << 7)) != 0;

        if (q) Vram0.Set(Coord.Make(0,0));
        if (r) Vram0.Set(Coord.Make(1,0));
        if (s) Vram0.Set(Coord.Make(2,0));
        if (t) Vram0.Set(Coord.Make(3,0));
        if (u) Vram0.Set(Coord.Make(4,0));
        if (v) Vram0.Set(Coord.Make(5,0));
        if (w) Vram0.Set(Coord.Make(6,0));
        if (x) Vram0.Set(Coord.Make(7,0));

        Cpu.Halt();
    }
}
