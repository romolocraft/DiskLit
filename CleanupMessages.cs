namespace DiskLit;

internal static class CleanupMessages
{
    public static string Limitation => new LocalText(
        "The entire selection is checked before starting. Windows cannot guarantee an atomic batch: a later change or error may leave some items in the Recycle Bin. There is no permanent-delete fallback.",
        "Toda a seleção é verificada antes de iniciar. O Windows não garante um lote atômico: uma alteração ou falha posterior pode deixar parte dos itens na Lixeira. Não há alternativa de exclusão permanente.",
        "Se comprueba toda la selección antes de empezar. Windows no garantiza un lote atómico: un cambio o error posterior puede dejar algunos elementos en la Papelera. No se recurre a la eliminación permanente.",
        "Перед началом проверяется весь набор. Windows не гарантирует атомарность: последующее изменение или ошибка могут оставить часть элементов в корзине. Безвозвратное удаление не используется.").ToString();

    public static string Failed(int removed) => new LocalText(
        $"Windows stopped the operation. {removed} items confirmed in the Recycle Bin. Check the original locations and the Recycle Bin before retrying.",
        $"O Windows interrompeu a operação. {removed} itens confirmados na Lixeira. Confira os locais originais e a Lixeira antes de tentar novamente.",
        $"Windows detuvo la operación. {removed} elementos confirmados en la Papelera. Revise las ubicaciones originales y la Papelera antes de reintentar.",
        $"Windows остановила операцию. Подтверждено в корзине: {removed}. Проверьте исходные папки и корзину перед повторной попыткой.").ToString();
}
