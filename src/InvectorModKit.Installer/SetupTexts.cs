using System;
using System.Collections.Generic;
using System.Linq;

namespace InvectorModKit.Installer
{
    /// <summary>The installer's texts in the game's 11 languages, picked from Windows' display language.</summary>
    public static class SetupTexts
    {
        public static readonly string[] Languages = { "en", "fr", "de", "it", "es", "pt-BR", "pl", "ru", "ja", "ko", "zh" };

        /// <summary>"fr-FR", "pt-PT", "zh-Hans-CN"… as one of ours; English otherwise.</summary>
        public static string Resolve(string culture)
        {
            string c = (culture ?? "").Trim();
            string exact = Languages.FirstOrDefault(l => string.Equals(l, c, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;
            string primary = c.Split('-', '_')[0].ToLowerInvariant();
            if (primary == "pt") return "pt-BR";
            return Languages.FirstOrDefault(l => l == primary) ?? "en";
        }

        public static IEnumerable<string> Keys => T.Keys;

        public static string Get(string lang, string key, params object[] args)
        {
            int i = Array.IndexOf(Languages, lang);
            if (!T.TryGetValue(key, out var row)) return key;
            string text = i >= 0 && i < row.Length && !string.IsNullOrEmpty(row[i]) ? row[i] : row[0];
            return args.Length == 0 ? text : string.Format(text, args);
        }

        // Columns: en, fr, de, it, es, pt-BR, pl, ru, ja, ko, zh
        private static readonly Dictionary<string, string[]> T = new Dictionary<string, string[]>
        {
            ["subtitle"] = new[] {
                "Custom songs, a chart marketplace and leaderboards for Invector: Rhythm Galaxy.",
                "Morceaux perso, marketplace de charts et classements pour Invector: Rhythm Galaxy.",
                "Eigene Songs, Chart-Marketplace und Ranglisten für Invector: Rhythm Galaxy.",
                "Brani personalizzati, marketplace di chart e classifiche per Invector: Rhythm Galaxy.",
                "Canciones personalizadas, marketplace de charts y clasificaciones para Invector: Rhythm Galaxy.",
                "Músicas personalizadas, marketplace de charts e rankings para Invector: Rhythm Galaxy.",
                "Własne utwory, marketplace chartów i rankingi do Invector: Rhythm Galaxy.",
                "Свои треки, маркетплейс чартов и рейтинги для Invector: Rhythm Galaxy.",
                "Invector: Rhythm Galaxy 用のカスタム曲、チャートマーケットプレイス、ランキング。",
                "Invector: Rhythm Galaxy용 커스텀 곡, 차트 마켓플레이스, 순위표.",
                "为 Invector: Rhythm Galaxy 提供自定义歌曲、谱面市场和排行榜。" },
            ["found"] = new[] { "Game found: {0}", "Jeu trouvé : {0}", "Spiel gefunden: {0}", "Gioco trovato: {0}", "Juego encontrado: {0}", "Jogo encontrado: {0}", "Znaleziono grę: {0}", "Игра найдена: {0}", "ゲームを検出: {0}", "게임 찾음: {0}", "已找到游戏：{0}" },
            ["notFound"] = new[] { "Game not found. Choose its folder.", "Jeu introuvable. Choisis son dossier.", "Spiel nicht gefunden. Wähle seinen Ordner.", "Gioco non trovato. Scegli la sua cartella.", "Juego no encontrado. Elige su carpeta.", "Jogo não encontrado. Escolha a pasta dele.", "Nie znaleziono gry. Wybierz jej folder.", "Игра не найдена. Выберите её папку.", "ゲームが見つかりません。フォルダーを選んでください。", "게임을 찾을 수 없습니다. 폴더를 선택하세요.", "未找到游戏。请选择其文件夹。" },
            ["change"] = new[] { "Change…", "Changer…", "Ändern…", "Cambia…", "Cambiar…", "Alterar…", "Zmień…", "Изменить…", "変更…", "변경…", "更改…" },
            ["pickTitle"] = new[] { "Choose the Invector Rhythm Galaxy folder", "Choisis le dossier d'Invector Rhythm Galaxy", "Wähle den Ordner von Invector Rhythm Galaxy", "Scegli la cartella di Invector Rhythm Galaxy", "Elige la carpeta de Invector Rhythm Galaxy", "Escolha a pasta do Invector Rhythm Galaxy", "Wybierz folder Invector Rhythm Galaxy", "Выберите папку Invector Rhythm Galaxy", "Invector Rhythm Galaxy のフォルダーを選択", "Invector Rhythm Galaxy 폴더 선택", "选择 Invector Rhythm Galaxy 文件夹" },
            ["editor"] = new[] { "Also install the level editor", "Installer aussi l'éditeur de niveaux", "Auch den Level-Editor installieren", "Installa anche l'editor di livelli", "Instalar también el editor de niveles", "Instalar também o editor de fases", "Zainstaluj też edytor poziomów", "Также установить редактор уровней", "レベルエディターもインストール", "레벨 에디터도 설치", "同时安装关卡编辑器" },
            ["install"] = new[] { "Install", "Installer", "Installieren", "Installa", "Instalar", "Instalar", "Zainstaluj", "Установить", "インストール", "설치", "安装" },
            ["update"] = new[] { "Update to {0}", "Mettre à jour vers {0}", "Auf {0} aktualisieren", "Aggiorna a {0}", "Actualizar a {0}", "Atualizar para {0}", "Zaktualizuj do {0}", "Обновить до {0}", "{0} に更新", "{0}(으)로 업데이트", "更新到 {0}" },
            ["repair"] = new[] { "Repair", "Réparer", "Reparieren", "Ripara", "Reparar", "Reparar", "Napraw", "Восстановить", "修復", "복구", "修复" },
            ["uninstall"] = new[] { "Uninstall", "Désinstaller", "Deinstallieren", "Disinstalla", "Desinstalar", "Desinstalar", "Odinstaluj", "Удалить", "アンインストール", "제거", "卸载" },
            ["installed"] = new[] { "Installed version: {0}", "Version installée : {0}", "Installierte Version: {0}", "Versione installata: {0}", "Versión instalada: {0}", "Versão instalada: {0}", "Zainstalowana wersja: {0}", "Установленная версия: {0}", "インストール済み: {0}", "설치된 버전: {0}", "已安装版本：{0}" },
            ["closeGame"] = new[] { "Close the game to continue.", "Ferme le jeu pour continuer.", "Schließe das Spiel, um fortzufahren.", "Chiudi il gioco per continuare.", "Cierra el juego para continuar.", "Feche o jogo para continuar.", "Zamknij grę, aby kontynuować.", "Закройте игру, чтобы продолжить.", "続けるにはゲームを閉じてください。", "계속하려면 게임을 닫으세요.", "请关闭游戏以继续。" },
            ["installing"] = new[] { "Installing…", "Installation…", "Wird installiert…", "Installazione…", "Instalando…", "Instalando…", "Instalowanie…", "Установка…", "インストール中…", "설치 중…", "正在安装…" },
            ["uninstalling"] = new[] { "Uninstalling…", "Désinstallation…", "Wird deinstalliert…", "Disinstallazione…", "Desinstalando…", "Desinstalando…", "Odinstalowywanie…", "Удаление…", "アンインストール中…", "제거 중…", "正在卸载…" },
            ["done"] = new[] { "All set!", "C'est prêt !", "Fertig!", "Tutto pronto!", "¡Listo!", "Tudo pronto!", "Gotowe!", "Готово!", "準備完了！", "준비 완료!", "完成！" },
            ["doneHint"] = new[] {
                "Start the game: the Marketplace is in the main menu, your songs go in the CustomSongs folder.",
                "Lance le jeu : le Marketplace est dans le menu principal, tes morceaux vont dans le dossier CustomSongs.",
                "Starte das Spiel: Der Marketplace ist im Hauptmenü, deine Songs kommen in den Ordner CustomSongs.",
                "Avvia il gioco: il Marketplace è nel menu principale, i tuoi brani vanno nella cartella CustomSongs.",
                "Inicia el juego: el Marketplace está en el menú principal, tus canciones van en la carpeta CustomSongs.",
                "Inicie o jogo: o Marketplace está no menu principal, suas músicas vão na pasta CustomSongs.",
                "Uruchom grę: Marketplace jest w menu głównym, twoje utwory trafiają do folderu CustomSongs.",
                "Запустите игру: Marketplace находится в главном меню, ваши треки кладите в папку CustomSongs.",
                "ゲームを起動してください。マーケットプレイスはメインメニューにあり、曲は CustomSongs フォルダーに入れます。",
                "게임을 실행하세요. 마켓플레이스는 메인 메뉴에 있고, 곡은 CustomSongs 폴더에 넣으면 됩니다.",
                "启动游戏：市场在主菜单中，你的歌曲放在 CustomSongs 文件夹里。" },
            ["play"] = new[] { "Play", "Lancer le jeu", "Spiel starten", "Gioca", "Jugar", "Jogar", "Graj", "Играть", "プレイ", "플레이", "开始游戏" },
            ["close"] = new[] { "Close", "Fermer", "Schließen", "Chiudi", "Cerrar", "Fechar", "Zamknij", "Закрыть", "閉じる", "닫기", "关闭" },
            ["uninstallConfirm"] = new[] {
                "Remove InvectorModKit from the game? Your songs stay in {0}.",
                "Retirer InvectorModKit du jeu ? Tes morceaux restent dans {0}.",
                "InvectorModKit aus dem Spiel entfernen? Deine Songs bleiben in {0}.",
                "Rimuovere InvectorModKit dal gioco? I tuoi brani restano in {0}.",
                "¿Quitar InvectorModKit del juego? Tus canciones se quedan en {0}.",
                "Remover o InvectorModKit do jogo? Suas músicas ficam em {0}.",
                "Usunąć InvectorModKit z gry? Twoje utwory zostaną w {0}.",
                "Удалить InvectorModKit из игры? Ваши треки останутся в {0}.",
                "InvectorModKit をゲームから削除しますか？曲は {0} に残ります。",
                "게임에서 InvectorModKit을 제거할까요? 곡은 {0}에 남습니다.",
                "要从游戏中移除 InvectorModKit 吗？你的歌曲会保留在 {0}。" },
            ["uninstalled"] = new[] {
                "Uninstalled. Your songs are kept in {0}.",
                "Désinstallé. Tes morceaux sont gardés dans {0}.",
                "Deinstalliert. Deine Songs bleiben in {0}.",
                "Disinstallato. I tuoi brani restano in {0}.",
                "Desinstalado. Tus canciones se conservan en {0}.",
                "Desinstalado. Suas músicas ficam guardadas em {0}.",
                "Odinstalowano. Twoje utwory zostały w {0}.",
                "Удалено. Ваши треки сохранены в {0}.",
                "アンインストールしました。曲は {0} に残っています。",
                "제거했습니다. 곡은 {0}에 그대로 있습니다.",
                "已卸载。你的歌曲保留在 {0}。" },
            ["err_not-game"] = new[] {
                "This is not the Invector Rhythm Galaxy folder.", "Ce dossier n'est pas celui d'Invector Rhythm Galaxy.", "Das ist nicht der Ordner von Invector Rhythm Galaxy.",
                "Questa non è la cartella di Invector Rhythm Galaxy.", "Esta no es la carpeta de Invector Rhythm Galaxy.", "Esta não é a pasta do Invector Rhythm Galaxy.",
                "To nie jest folder Invector Rhythm Galaxy.", "Это не папка Invector Rhythm Galaxy.", "これは Invector Rhythm Galaxy のフォルダーではありません。",
                "Invector Rhythm Galaxy 폴더가 아닙니다.", "这不是 Invector Rhythm Galaxy 的文件夹。" },
            ["err_bepinex6"] = new[] {
                "Another mod loader (BepInEx 6) is installed. InvectorModKit needs BepInEx 5.",
                "Un autre chargeur de mods (BepInEx 6) est installé. InvectorModKit a besoin de BepInEx 5.",
                "Ein anderer Mod-Loader (BepInEx 6) ist installiert. InvectorModKit braucht BepInEx 5.",
                "È installato un altro caricatore di mod (BepInEx 6). InvectorModKit richiede BepInEx 5.",
                "Hay otro cargador de mods instalado (BepInEx 6). InvectorModKit necesita BepInEx 5.",
                "Outro carregador de mods (BepInEx 6) está instalado. O InvectorModKit precisa do BepInEx 5.",
                "Zainstalowano inny loader modów (BepInEx 6). InvectorModKit wymaga BepInEx 5.",
                "Установлен другой загрузчик модов (BepInEx 6). InvectorModKit нужен BepInEx 5.",
                "別の MOD ローダー（BepInEx 6）が入っています。InvectorModKit には BepInEx 5 が必要です。",
                "다른 모드 로더(BepInEx 6)가 설치되어 있습니다. InvectorModKit에는 BepInEx 5가 필요합니다.",
                "已安装另一个模组加载器（BepInEx 6）。InvectorModKit 需要 BepInEx 5。" },
            ["err_in-use"] = new[] {
                "A game file is in use. Close the game and try again.", "Un fichier du jeu est utilisé. Ferme le jeu et réessaie.",
                "Eine Spieldatei wird verwendet. Schließe das Spiel und versuche es erneut.", "Un file del gioco è in uso. Chiudi il gioco e riprova.",
                "Un archivo del juego está en uso. Cierra el juego e inténtalo de nuevo.", "Um arquivo do jogo está em uso. Feche o jogo e tente de novo.",
                "Plik gry jest używany. Zamknij grę i spróbuj ponownie.", "Файл игры занят. Закройте игру и попробуйте снова.",
                "ゲームのファイルが使用中です。ゲームを閉じてもう一度試してください。", "게임 파일이 사용 중입니다. 게임을 닫고 다시 시도하세요.",
                "游戏文件正在使用中。请关闭游戏后重试。" },
            ["err_other"] = new[] {
                "The installation could not finish ({0}). If your antivirus blocked a file, allow it, or use the manual zip from the download page.",
                "L'installation n'a pas pu se terminer ({0}). Si ton antivirus a bloqué un fichier, autorise-le, ou utilise le zip manuel de la page de téléchargement.",
                "Die Installation konnte nicht abgeschlossen werden ({0}). Wenn dein Virenschutz eine Datei blockiert hat, erlaube sie oder nutze das manuelle Zip von der Downloadseite.",
                "L'installazione non è riuscita a terminare ({0}). Se l'antivirus ha bloccato un file, consentilo oppure usa lo zip manuale dalla pagina di download.",
                "La instalación no pudo terminar ({0}). Si tu antivirus bloqueó un archivo, permítelo o usa el zip manual de la página de descarga.",
                "A instalação não pôde terminar ({0}). Se o seu antivírus bloqueou um arquivo, permita-o ou use o zip manual da página de download.",
                "Instalacja nie mogła się zakończyć ({0}). Jeśli antywirus zablokował plik, zezwól na niego lub użyj ręcznego zipa ze strony pobierania.",
                "Установка не завершилась ({0}). Если антивирус заблокировал файл, разрешите его или используйте zip для ручной установки со страницы загрузки.",
                "インストールを完了できませんでした（{0}）。ウイルス対策ソフトがファイルをブロックした場合は許可するか、ダウンロードページの手動インストール用 zip を使ってください。",
                "설치를 끝내지 못했습니다({0}). 백신이 파일을 차단했다면 허용하거나 다운로드 페이지의 수동 설치용 zip을 사용하세요.",
                "安装未能完成（{0}）。如果杀毒软件拦截了文件，请允许它，或使用下载页面上的手动安装 zip。" },
        };
    }
}
