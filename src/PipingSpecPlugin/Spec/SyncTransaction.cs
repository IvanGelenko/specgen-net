using System;
using System.Collections.Generic;
namespace PipingSpecPlugin.Spec
{
    public static class SyncTransaction
    {
        public static List<string> Run(Action begin, Action write, Action accept, Action commit, Action rollback)
        {
            var errors = new List<string>();
            bool started = false;
            try
            {
                begin();
                started = true;
                write();
                accept();
                commit();
            }
            catch (Exception ex)
            {
                errors.Add($"Операция не завершена: {ex.Message}");
                if (started)
                {
                    try { rollback(); }
                    catch (Exception rollbackError) { errors.Add($"Не удалось откатить изменения: {rollbackError.Message}. Проверьте состояние проекта."); }
                }
            }
            return errors;
        }
    }
}
