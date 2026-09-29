using System.Reflection;
using System.Resources;
using System.Runtime.InteropServices;

// Метаданные версии → попадают в свойства .exe (вкладка «Подробно»):
// Издатель, Продукт, Описание, Версия, Авторские права.
// Пустые метаданные + права админа + запись в реестр = высокий «подозрительный»
// балл у эвристики Defender. Заполненные метаданные снижают ложные срабатывания.

[assembly: AssemblyTitle("BUL$ Opti Optimizer")]
[assembly: AssemblyProduct("BUL$ Opti Optimizer")]
[assembly: AssemblyDescription("Оптимизатор игрового ПК — безопасные обратимые твики Windows")]
[assembly: AssemblyCompany("BUL$ Opti")]
[assembly: AssemblyCopyright("© 2026 BUL$ Opti · bulsopti.com")]
[assembly: AssemblyTrademark("BUL$ Opti")]
[assembly: AssemblyConfiguration("")]
[assembly: NeutralResourcesLanguage("ru-RU")]

[assembly: AssemblyVersion("3.0.0.0")]
[assembly: AssemblyFileVersion("3.0.0.0")]
[assembly: AssemblyInformationalVersion("3.0")]

[assembly: ComVisible(false)]
