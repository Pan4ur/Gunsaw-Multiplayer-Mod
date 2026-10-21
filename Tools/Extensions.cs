/*
    Классы полны null'ов, но время не вернуть вспять
    На деревянный # можешь распять .NET раз пять
    C Kotlin'а спастил Let, мне null уже не понять
    Отомсти мне за всё, аллоки запиши в тетрадь
    Классы полны лямбд, я ебал писать if
    На деревянный # можешь распять Roslyn пока он жив
    Не инлайни Let, пусть охуеет JIT
    Отомсти мне за всё, а git всё сохранит
*/
public static class Extensions
{
    public static void Let<T>(this T? val, Action<T> action) where T : class
    {
        if (val is not null) action(val);
    }
}