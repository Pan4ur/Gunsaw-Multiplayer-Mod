/// <summary>
/// Controls 8bit CPU program execution
/// </summary>
/// <remarks>
/// These methods are compiler intrinsics: <c>build.py</c> maps them to physical CPU signals
/// </remarks>
public static class Cpu
{
    /// <summary>Inserts the requested number of empty CPU states</summary>
    /// <param name="cycles">Number of wait cycles</param>
    /// <remarks>Each cycle is a separate wide-state barrier. Use it after a VRAM write when a frame must become visible before the next update</remarks>
    public static void Wait(int cycles) { }

    /// <summary>Stops the program in its current state</summary>
    /// <remarks>In hardware, the PC remains in the halt state</remarks>
    public static void Halt() { }
}

/// <summary>Provides access to the 32 8bit RAM cells</summary>
public static class Ram
{
    /// <summary>Reads a byte from RAM</summary>
    /// <param name="address">Address; hardware uses its low 5 bits</param>
    /// <returns>The 8bit contents of the selected cell</returns>
    /// <remarks>A read is a physical load operation. The compiler assigns dependent reads their own state, so its result is safe to use in the following operation</remarks>
    public static byte Read(int address) => 0;

    /// <summary>Writes a byte to RAM</summary>
    /// <param name="address">Address; hardware uses its low 5 bits</param>
    /// <param name="value">Value; hardware uses its low 8 bits</param>
    /// <remarks>The write is committed on the CPUCLK edge through separate RAM address and data buses</remarks>
    public static void Write(int address, int value) { }
}

/// <summary>An 8×8 1bit P0 video plane</summary>
public static class Vram0
{
    /// <summary>Gets the P0 pixel state at an address</summary>
    /// <param name="address">Coordinate in <c>0x0Y0X</c> format</param>
    /// <returns><see langword="true"/> when the pixel is set</returns>
    /// <remarks>The compiler treats a VRAM read as a separate load operation</remarks>
    public static bool Read(int address) => false;

    /// <summary>Sets a P0 pixel</summary>
    /// <param name="address">Coordinate in <c>0x0Y0X</c> format</param>
    public static void Set(int address) { }

    /// <summary>Clears a P0 pixel</summary>
    /// <param name="address">Coordinate in <c>0x0Y0X</c> format</param>
    public static void Clear(int address) { }

    /// <summary>Clears all 64 P0 pixels</summary>
    public static void ClearAll() { }
}

/// <summary>An 8×8 1bit P1 video plane</summary>
public static class Vram1
{
    /// <summary>Gets the P1 pixel state at an address</summary>
    /// <param name="address">Coordinate in <c>0x0Y0X</c> format</param>
    /// <returns><see langword="true"/> when the pixel is set</returns>
    /// <remarks>The compiler treats a VRAM read as a separate load operation</remarks>
    public static bool Read(int address) => false;

    /// <summary>Sets a P1 pixel</summary>
    /// <param name="address">Coordinate in <c>0x0Y0X</c> format</param>
    public static void Set(int address) { }

    /// <summary>Clears a P1 pixel</summary>
    /// <param name="address">Coordinate in <c>0x0Y0X</c> format</param>
    public static void Clear(int address) { }

    /// <summary>Clears all 64 P1 pixels</summary>
    public static void ClearAll() { }
}

/// <summary>Converts between 8×8 coordinates and their hardware representation</summary>
public static class Coord
{
    /// <summary>Packs a cell coordinate into <c>0x0Y0X</c> format</summary>
    public static byte Make(int x, int y) => (byte)(((y & 7) << 4) | (x & 7));

    /// <summary>Extracts X from a packed coordinate.</summary>
    public static byte X(int coordinate) => (byte)(coordinate & 7);

    /// <summary>Extracts Y from a packed coordinate.</summary>
    public static byte Y(int coordinate) => (byte)((coordinate >> 4) & 7);
}

/// <summary>Physical buttons and directional pad</summary>
public static class InputKeys
{
    /// <summary>Gets the current D-pad direction: 0 = right, 1 = down, 2 = left, 3 = up</summary>
    public static byte Direction => 0;

    /// <summary>Gets the Up button pulse in the current CPU state</summary>
    public static bool UpPressed => false;

    /// <summary>Gets the Down button pulse in the current CPU state</summary>
    public static bool DownPressed => false;

    /// <summary>Gets the Left button pulse in the current CPU state</summary>
    public static bool LeftPressed => false;

    /// <summary>Gets the Right button pulse in the current CPU state</summary>
    public static bool RightPressed => false;
}

/// <summary>Provides the 8bit hardware RNG</summary>
public static class Random
{
    /// <summary>Gets the current 8bit RNG value</summary>
    public static byte Byte => 0;
}

/// <summary>Hardware first-step path search on the 8×8 grid</summary>
public static class Pathfinder
{
    /// <summary>Finds the direction of the first step from the head to the target</summary>
    /// <param name="head">Starting coordinate in <c>0x0Y0X</c> format</param>
    /// <param name="food">Target coordinate in <c>0x0Y0X</c> format</param>
    /// <returns>0 = right, 1 = down, 2 = left, 3 = up</returns>
    /// <remarks>Hardware BFS treats set P0 pixels as obstacles. The call holds the current CPU state until a result is captured; equal-length routes use fixed priority: right, down, left, then up.</remarks>
    public static byte Find(byte head, byte food) => 0;
}
