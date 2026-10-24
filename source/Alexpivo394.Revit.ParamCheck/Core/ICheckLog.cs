namespace Alexpivo394.Revit.ParamCheck.Core;

/// <summary>Журнал проверки: файл у ручного запуска, поток событий агенту ToadFlow.</summary>
public interface ICheckLog
{
    void Step(string model, string text);

    /// <summary>Непредвиденная ошибка — с исключением целиком, чтобы по журналу можно было найти причину.</summary>
    void Error(string model, string text, Exception exception);
}
