namespace Alexpivo394.Revit.ParamCheck.Core;

/// <summary>Куда проверка сообщает о ходе работы с моделью и откуда узнаёт, что её просят прервать.</summary>
public interface ICheckProgress
{
    /// <summary>Текущий шаг, например «Открытие модели»; null — без подписи.</summary>
    void Status(string? status);

    /// <summary>Ход долгой операции внутри шага: открытие модели в Revit или перебор элементов правила.</summary>
    void Report(int current, int total, string? status);

    /// <summary>Модель просят бросить: пропустить, прервать проверку или отменить задание.</summary>
    bool ShouldAbort { get; }
}
