using System.Collections.Generic;
using HelloThere.Globalization;

namespace InvectorModKit.Marketplace
{
    /// <summary>
    /// The marketplace's texts in the game's 11 languages, picked from the language set in the game's options
    /// (Globalizer.selectedLanguage). Columns: en, fr, de, it, es, pt-BR, pl, ru, ja, ko, zh-CN.
    /// </summary>
    internal static class Loc
    {
        private static int Column
        {
            get
            {
                switch (Globalizer.selectedLanguage)
                {
                    case Globalizer.Language.French: return 1;
                    case Globalizer.Language.German: return 2;
                    case Globalizer.Language.Italian: return 3;
                    case Globalizer.Language.Spanish: return 4;
                    case Globalizer.Language.Portuguese: return 5;
                    case Globalizer.Language.Polish: return 6;
                    case Globalizer.Language.Russian: return 7;
                    case Globalizer.Language.Japanese: return 8;
                    case Globalizer.Language.Korean: return 9;
                    case Globalizer.Language.ChineseSimplified: return 10;
                    default: return 0;
                }
            }
        }

        public static string T(string key)
        {
            if (!Strings.TryGetValue(key, out var row)) return key;
            int c = Column;
            return c < row.Length && !string.IsNullOrEmpty(row[c]) ? row[c] : row[0];
        }

        public static string T(string key, params object[] args) => string.Format(T(key), args);

        /// <summary>"1 download" / "3 downloads" (French uses the singular for 0 and 1).</summary>
        public static string Downloads(int n) =>
            T(n == 1 || (n == 0 && Globalizer.selectedLanguage == Globalizer.Language.French) ? "downloads_one" : "downloads_many", n);

        private static readonly Dictionary<string, string[]> Strings = new Dictionary<string, string[]>
        {
            ["title"] = new[] { "MARKETPLACE", "MARKETPLACE", "MARKETPLACE", "MARKETPLACE", "MARKETPLACE", "MARKETPLACE", "MARKETPLACE", "МАРКЕТПЛЕЙС", "マーケットプレイス", "마켓플레이스", "市场" },
            ["menu_label"] = new[] { "Marketplace", "Marketplace", "Marketplace", "Marketplace", "Marketplace", "Marketplace", "Marketplace", "Маркетплейс", "マーケットプレイス", "마켓플레이스", "市场" },
            ["sign_in"] = new[] { "SIGN IN", "SE CONNECTER", "ANMELDEN", "ACCEDI", "INICIAR SESIÓN", "ENTRAR", "ZALOGUJ", "ВОЙТИ", "ログイン", "로그인", "登录" },
            ["signed_in"] = new[] { "Signed in: {0}", "Connecté : {0}", "Angemeldet: {0}", "Connesso: {0}", "Conectado: {0}", "Conectado: {0}", "Zalogowano: {0}", "Вход выполнен: {0}", "ログイン中：{0}", "로그인: {0}", "已登录：{0}" },
            ["admin_suffix"] = new[] { " (admin)", " (admin)", " (Admin)", " (admin)", " (admin)", " (admin)", " (admin)", " (админ)", "（管理者）", " (관리자)", "（管理员）" },
            ["signing_in"] = new[] { "Signing in through your browser…", "Connexion dans ton navigateur…", "Anmeldung im Browser…", "Accesso nel browser…", "Iniciando sesión en tu navegador…", "Entrando pelo navegador…", "Logowanie w przeglądarce…", "Вход через браузер…", "ブラウザでログイン中…", "브라우저에서 로그인 중…", "正在浏览器中登录…" },
            ["sign_in_failed"] = new[] { "Couldn't sign in: {0}", "Connexion impossible : {0}", "Anmeldung fehlgeschlagen: {0}", "Accesso non riuscito: {0}", "No se pudo iniciar sesión: {0}", "Não foi possível entrar: {0}", "Nie udało się zalogować: {0}", "Не удалось войти: {0}", "ログインできません：{0}", "로그인 실패: {0}", "无法登录：{0}" },
            ["tab_browse"] = new[] { "BROWSE", "PARCOURIR", "DURCHSUCHEN", "SFOGLIA", "EXPLORAR", "EXPLORAR", "PRZEGLĄDAJ", "КАТАЛОГ", "探す", "둘러보기", "浏览" },
            ["tab_mine"] = new[] { "MY SONGS", "MES MORCEAUX", "MEINE SONGS", "I MIEI BRANI", "MIS CANCIONES", "MINHAS MÚSICAS", "MOJE UTWORY", "МОИ ТРЕКИ", "マイソング", "내 곡", "我的歌曲" },
            ["hints"] = new[]
            {
                "ESC  Close      TAB  Switch tab      F  Search      ENTER or DOUBLE-CLICK  Main action      ← →  Actions",
                "ÉCHAP  Fermer      TAB  Changer d'onglet      F  Rechercher      ENTRÉE ou DOUBLE-CLIC  Action principale      ← →  Actions",
                "ESC  Schließen      TAB  Tab wechseln      F  Suchen      ENTER oder DOPPELKLICK  Hauptaktion      ← →  Aktionen",
                "ESC  Chiudi      TAB  Cambia scheda      F  Cerca      INVIO o DOPPIO CLIC  Azione principale      ← →  Azioni",
                "ESC  Cerrar      TAB  Cambiar pestaña      F  Buscar      INTRO o DOBLE CLIC  Acción principal      ← →  Acciones",
                "ESC  Fechar      TAB  Trocar aba      F  Buscar      ENTER ou CLIQUE DUPLO  Ação principal      ← →  Ações",
                "ESC  Zamknij      TAB  Zmień kartę      F  Szukaj      ENTER lub DWUKLIK  Główna akcja      ← →  Akcje",
                "ESC  Закрыть      TAB  Сменить вкладку      F  Поиск      ENTER или ДВОЙНОЙ ЩЕЛЧОК  Основное действие      ← →  Действия",
                "ESC  閉じる      TAB  タブ切り替え      F  検索      ENTER / ダブルクリック  決定      ← →  操作",
                "ESC  닫기      TAB  탭 전환      F  검색      ENTER 또는 더블클릭  기본 동작      ← →  동작",
                "ESC  关闭      TAB  切换标签      F  搜索      ENTER 或双击  主要操作      ← →  操作",
            },
            ["confirm"] = new[] { "CONFIRM", "CONFIRMER", "BESTÄTIGEN", "CONFERMA", "CONFIRMAR", "CONFIRMAR", "POTWIERDŹ", "ПОДТВЕРДИТЬ", "確認", "확인", "确认" },
            ["cancel"] = new[] { "CANCEL", "ANNULER", "ABBRECHEN", "ANNULLA", "CANCELAR", "CANCELAR", "ANULUJ", "ОТМЕНА", "キャンセル", "취소", "取消" },
            ["ok"] = new[] { "OK", "VALIDER", "OK", "OK", "ACEPTAR", "OK", "OK", "ОК", "OK", "확인", "确定" },
            ["sort_recent"] = new[] { "RECENT", "RÉCENTS", "NEUESTE", "RECENTI", "RECIENTES", "RECENTES", "NAJNOWSZE", "НОВЫЕ", "新着", "최신", "最新" },
            ["sort_popular"] = new[] { "POPULAR", "POPULAIRES", "BELIEBT", "POPOLARI", "POPULARES", "POPULARES", "POPULARNE", "ПОПУЛЯРНЫЕ", "人気", "인기", "热门" },
            ["more_results"] = new[] { "MORE RESULTS", "PLUS DE RÉSULTATS", "MEHR ERGEBNISSE", "ALTRI RISULTATI", "MÁS RESULTADOS", "MAIS RESULTADOS", "WIĘCEJ WYNIKÓW", "ЕЩЁ", "もっと見る", "더 보기", "更多结果" },
            ["search_hint"] = new[] { "F  SEARCH A TITLE, ARTIST OR AUTHOR", "F  RECHERCHER UN TITRE, UN ARTISTE, UN AUTEUR", "F  TITEL, KÜNSTLER ODER AUTOR SUCHEN", "F  CERCA TITOLO, ARTISTA O AUTORE", "F  BUSCAR TÍTULO, ARTISTA O AUTOR", "F  BUSCAR TÍTULO, ARTISTA OU AUTOR", "F  SZUKAJ TYTUŁU, WYKONAWCY LUB AUTORA", "F  ПОИСК ПО НАЗВАНИЮ, АРТИСТУ, АВТОРУ", "F  曲名・アーティスト・作者で検索", "F  제목, 아티스트, 제작자 검색", "F  搜索标题、艺术家或作者" },
            ["search_query"] = new[] { "SEARCH: {0}", "RECHERCHE : {0}", "SUCHE: {0}", "RICERCA: {0}", "BÚSQUEDA: {0}", "BUSCA: {0}", "SZUKAJ: {0}", "ПОИСК: {0}", "検索：{0}", "검색: {0}", "搜索：{0}" },
            ["searching"] = new[] { "Searching…", "Recherche…", "Suche…", "Ricerca…", "Buscando…", "Buscando…", "Wyszukiwanie…", "Поиск…", "検索中…", "검색 중…", "搜索中…" },
            ["no_match"] = new[] { "No chart matches.", "Aucun chart ne correspond.", "Kein passender Chart.", "Nessun chart corrispondente.", "Ningún chart coincide.", "Nenhum chart encontrado.", "Brak pasujących chartów.", "Ничего не найдено.", "該当するチャートはありません。", "일치하는 차트가 없습니다.", "没有匹配的谱面。" },
            ["no_charts"] = new[] { "No charts published yet.", "Aucun chart publié pour l'instant.", "Noch keine Charts veröffentlicht.", "Ancora nessun chart pubblicato.", "Aún no hay charts publicados.", "Nenhum chart publicado ainda.", "Brak opublikowanych chartów.", "Пока нет опубликованных чартов.", "公開されたチャートはまだありません。", "아직 공개된 차트가 없습니다.", "暂无已发布的谱面。" },
            ["unreachable"] = new[] { "Marketplace unreachable: {0}", "Marketplace injoignable : {0}", "Marketplace nicht erreichbar: {0}", "Marketplace non raggiungibile: {0}", "Marketplace no disponible: {0}", "Marketplace indisponível: {0}", "Marketplace niedostępny: {0}", "Маркетплейс недоступен: {0}", "マーケットプレイスに接続できません：{0}", "마켓플레이스에 연결할 수 없습니다: {0}", "无法连接市场：{0}" },
            ["audio_header"] = new[] { "AUDIO: THE LINKED YOUTUBE VIDEO", "AUDIO : LA VIDÉO YOUTUBE LIÉE", "AUDIO: DAS VERKNÜPFTE YOUTUBE-VIDEO", "AUDIO: IL VIDEO YOUTUBE COLLEGATO", "AUDIO: EL VÍDEO DE YOUTUBE ENLAZADO", "ÁUDIO: O VÍDEO DO YOUTUBE VINCULADO", "AUDIO: POWIĄZANY FILM Z YOUTUBE", "АУДИО: ПРИВЯЗАННОЕ ВИДЕО YOUTUBE", "音源：リンクされたYouTube動画", "음원: 연결된 YouTube 영상", "音频：关联的 YouTube 视频" },
            ["audio_note"] = new[]
            {
                "The chart is timed on this video's soundtrack: this is the version of the song you need.",
                "Le chart est calé sur la bande-son de cette vidéo : c'est cette version du morceau qu'il te faut.",
                "Der Chart ist auf den Ton dieses Videos abgestimmt: Du brauchst genau diese Version des Songs.",
                "Il chart è sincronizzato sull'audio di questo video: ti serve questa versione del brano.",
                "El chart está sincronizado con el audio de este vídeo: necesitas esta versión de la canción.",
                "O chart está sincronizado com o áudio deste vídeo: você precisa desta versão da música.",
                "Chart jest zsynchronizowany z dźwiękiem tego filmu: potrzebujesz tej wersji utworu.",
                "Чарт синхронизирован со звуком этого видео: нужна именно эта версия трека.",
                "このチャートはこの動画の音声に合わせてあります。このバージョンの曲が必要です。",
                "이 차트는 이 영상의 음원에 맞춰져 있습니다. 이 버전의 곡이 필요합니다.",
                "该谱面与此视频的音轨对齐：你需要的正是这个版本的歌曲。",
            },
            ["install"] = new[] { "INSTALL", "INSTALLER", "INSTALLIEREN", "INSTALLA", "INSTALAR", "INSTALAR", "ZAINSTALUJ", "УСТАНОВИТЬ", "インストール", "설치", "安装" },
            ["open_youtube"] = new[] { "OPEN ON YOUTUBE", "OUVRIR SUR YOUTUBE", "AUF YOUTUBE ÖFFNEN", "APRI SU YOUTUBE", "ABRIR EN YOUTUBE", "ABRIR NO YOUTUBE", "OTWÓRZ W YOUTUBE", "ОТКРЫТЬ НА YOUTUBE", "YouTubeで開く", "YouTube에서 열기", "在 YouTube 打开" },
            ["open_folder"] = new[] { "CUSTOMSONGS FOLDER", "DOSSIER CUSTOMSONGS", "ORDNER CUSTOMSONGS", "CARTELLA CUSTOMSONGS", "CARPETA CUSTOMSONGS", "PASTA CUSTOMSONGS", "FOLDER CUSTOMSONGS", "ПАПКА CUSTOMSONGS", "CustomSongsフォルダー", "CustomSongs 폴더", "CustomSongs 文件夹" },
            ["remove"] = new[] { "REMOVE", "RETIRER", "ENTFERNEN", "RIMUOVI", "RETIRAR", "REMOVER", "USUŃ", "УДАЛИТЬ", "削除", "삭제", "移除" },
            ["quoted"] = new[] { "“{0}”", "« {0} »", "„{0}“", "«{0}»", "«{0}»", "“{0}”", "„{0}”", "«{0}»", "「{0}」", "“{0}”", "“{0}”" },
            ["by_author"] = new[] { "{0}  ·  by {1}", "{0}  ·  par {1}", "{0}  ·  von {1}", "{0}  ·  di {1}", "{0}  ·  de {1}", "{0}  ·  por {1}", "{0}  ·  autor: {1}", "{0}  ·  автор: {1}", "{0}  ·  作者：{1}", "{0}  ·  제작: {1}", "{0}  ·  作者：{1}" },
            ["mine"] = new[] { "YOUR CHART", "TON CHART", "DEIN CHART", "IL TUO CHART", "TU CHART", "SEU CHART", "TWÓJ CHART", "ВАШ ЧАРТ", "自分のチャート", "내 차트", "你的谱面" },
            ["installed"] = new[] { "INSTALLED", "INSTALLÉ", "INSTALLIERT", "INSTALLATO", "INSTALADO", "INSTALADO", "ZAINSTALOWANO", "УСТАНОВЛЕН", "インストール済み", "설치됨", "已安装" },
            ["waiting"] = new[] { "WAITING", "EN ATTENTE", "WARTET", "IN ATTESA", "EN ESPERA", "AGUARDANDO", "OCZEKUJE", "ОЖИДАНИЕ", "待機中", "대기 중", "等待中" },
            ["waiting_audio"] = new[] { "WAITING FOR THE AUDIO", "EN ATTENTE DE L'AUDIO", "WARTET AUF DAS AUDIO", "IN ATTESA DELL'AUDIO", "ESPERANDO EL AUDIO", "AGUARDANDO O ÁUDIO", "CZEKA NA AUDIO", "ОЖИДАНИЕ АУДИО", "音源待ち", "음원 대기 중", "等待音频" },
            ["chart_by"] = new[] { "Chart by {0}  ·  version {1}  ·  {2} BPM  ·  {3}", "Chart de {0}  ·  version {1}  ·  {2} BPM  ·  {3}", "Chart von {0}  ·  Version {1}  ·  {2} BPM  ·  {3}", "Chart di {0}  ·  versione {1}  ·  {2} BPM  ·  {3}", "Chart de {0}  ·  versión {1}  ·  {2} BPM  ·  {3}", "Chart de {0}  ·  versão {1}  ·  {2} BPM  ·  {3}", "Chart: {0}  ·  wersja {1}  ·  {2} BPM  ·  {3}", "Чарт: {0}  ·  версия {1}  ·  {2} BPM  ·  {3}", "作者：{0}  ·  バージョン {1}  ·  {2} BPM  ·  {3}", "제작: {0}  ·  버전 {1}  ·  {2} BPM  ·  {3}", "谱面作者：{0}  ·  版本 {1}  ·  {2} BPM  ·  {3}" },
            ["notes_line"] = new[] { "Notes: Casual {0}  ·  Normal {1}  ·  Hard {2}  ·  Ultra {3}", "Notes : Casual {0}  ·  Normal {1}  ·  Hard {2}  ·  Ultra {3}", "Noten: Casual {0}  ·  Normal {1}  ·  Hard {2}  ·  Ultra {3}", "Note: Casual {0}  ·  Normal {1}  ·  Hard {2}  ·  Ultra {3}", "Notas: Casual {0}  ·  Normal {1}  ·  Hard {2}  ·  Ultra {3}", "Notas: Casual {0}  ·  Normal {1}  ·  Hard {2}  ·  Ultra {3}", "Nuty: Casual {0}  ·  Normal {1}  ·  Hard {2}  ·  Ultra {3}", "Ноты: Casual {0}  ·  Normal {1}  ·  Hard {2}  ·  Ultra {3}", "ノーツ：Casual {0}  ·  Normal {1}  ·  Hard {2}  ·  Ultra {3}", "노트: Casual {0}  ·  Normal {1}  ·  Hard {2}  ·  Ultra {3}", "音符：Casual {0}  ·  Normal {1}  ·  Hard {2}  ·  Ultra {3}" },
            ["downloads_one"] = new[] { "{0} download", "{0} téléchargement", "{0} Download", "{0} download", "{0} descarga", "{0} download", "Pobrania: {0}", "Загрузок: {0}", "ダウンロード数：{0}", "다운로드 {0}회", "下载 {0} 次" },
            ["downloads_many"] = new[] { "{0} downloads", "{0} téléchargements", "{0} Downloads", "{0} download", "{0} descargas", "{0} downloads", "Pobrania: {0}", "Загрузок: {0}", "ダウンロード数：{0}", "다운로드 {0}회", "下载 {0} 次" },
            ["drop_hint"] = new[]
            {
                "Drop this song (the version of the linked video) into the CustomSongs folder: the chart will line up with it on its own.",
                "Dépose ce morceau (la version de la vidéo liée) dans le dossier CustomSongs : le chart s'y calera tout seul.",
                "Leg diesen Song (die Version des verknüpften Videos) in den Ordner CustomSongs: Der Chart passt sich automatisch an.",
                "Metti questo brano (la versione del video collegato) nella cartella CustomSongs: il chart si allineerà da solo.",
                "Pon esta canción (la versión del vídeo enlazado) en la carpeta CustomSongs: el chart se ajustará solo.",
                "Coloque esta música (a versão do vídeo vinculado) na pasta CustomSongs: o chart se ajusta sozinho.",
                "Wrzuć ten utwór (wersję z powiązanego filmu) do folderu CustomSongs: chart dopasuje się sam.",
                "Положите этот трек (версию из привязанного видео) в папку CustomSongs: чарт подстроится сам.",
                "この曲（リンクされた動画のバージョン）をCustomSongsフォルダーに入れてください。チャートは自動で合わせられます。",
                "이 곡(연결된 영상의 버전)을 CustomSongs 폴더에 넣으세요. 차트가 자동으로 맞춰집니다.",
                "将这首歌（关联视频的版本）放入 CustomSongs 文件夹：谱面会自动对齐。",
            },
            ["installed_done"] = new[] { "Installed! It's in song select, under Custom Songs.", "Installé ! Il est dans la sélection, section Custom Songs.", "Installiert! Du findest ihn in der Songauswahl unter Custom Songs.", "Installato! Lo trovi nella selezione brani, in Custom Songs.", "¡Instalado! Está en la selección de canciones, en Custom Songs.", "Instalado! Está na seleção de músicas, em Custom Songs.", "Zainstalowano! Znajdziesz go w wyborze utworów, w sekcji Custom Songs.", "Установлено! Ищите в выборе треков, раздел Custom Songs.", "インストールしました！曲選択のCustom Songsにあります。", "설치 완료! 곡 선택의 Custom Songs에 있습니다.", "已安装！在选歌界面的 Custom Songs 中。" },
            ["downloading"] = new[] { "Downloading the chart…", "Téléchargement du chart…", "Chart wird heruntergeladen…", "Download del chart…", "Descargando el chart…", "Baixando o chart…", "Pobieranie chartu…", "Загрузка чарта…", "チャートをダウンロード中…", "차트 다운로드 중…", "正在下载谱面…" },
            ["downloaded"] = new[]
            {
                "Downloaded. Looking for this song among your files… If it isn't there, drop it (the version of the linked video) into CustomSongs.",
                "Téléchargé. Je cherche ce morceau parmi tes fichiers… S'il n'y est pas, dépose-le (la version de la vidéo liée) dans CustomSongs.",
                "Heruntergeladen. Ich suche diesen Song in deinen Dateien… Falls er fehlt, leg ihn (die Version des verknüpften Videos) in CustomSongs.",
                "Scaricato. Cerco il brano tra i tuoi file… Se non c'è, mettilo (la versione del video collegato) in CustomSongs.",
                "Descargado. Buscando la canción entre tus archivos… Si no está, ponla (la versión del vídeo enlazado) en CustomSongs.",
                "Baixado. Procurando a música nos seus arquivos… Se não estiver lá, coloque-a (a versão do vídeo vinculado) em CustomSongs.",
                "Pobrano. Szukam utworu wśród twoich plików… Jeśli go nie ma, wrzuć go (wersję z powiązanego filmu) do CustomSongs.",
                "Загружено. Ищу трек среди ваших файлов… Если его нет, положите его (версию из видео) в CustomSongs.",
                "ダウンロードしました。ファイルからこの曲を探しています… 見つからない場合は、リンクされた動画のバージョンをCustomSongsに入れてください。",
                "다운로드했습니다. 파일에서 이 곡을 찾는 중… 없으면 연결된 영상 버전을 CustomSongs에 넣으세요.",
                "已下载。正在你的文件中查找这首歌… 如果没有，请将关联视频的版本放入 CustomSongs。",
            },
            ["download_failed"] = new[] { "Download failed: {0}", "Échec du téléchargement : {0}", "Download fehlgeschlagen: {0}", "Download non riuscito: {0}", "Error de descarga: {0}", "Falha no download: {0}", "Pobieranie nie powiodło się: {0}", "Ошибка загрузки: {0}", "ダウンロード失敗：{0}", "다운로드 실패: {0}", "下载失败：{0}" },
            ["remove_confirm"] = new[] { "Remove “{0}” ({1}) from the marketplace?\nPlayers who installed it keep it.", "Retirer « {0} » ({1}) du marketplace ?\nLes joueurs qui l'ont installé le gardent.", "„{0}“ ({1}) aus dem Marketplace entfernen?\nWer ihn installiert hat, behält ihn.", "Rimuovere «{0}» ({1}) dal marketplace?\nChi l'ha installato lo conserva.", "¿Retirar «{0}» ({1}) del marketplace?\nQuien lo instaló lo conserva.", "Remover “{0}” ({1}) do marketplace?\nQuem instalou continua com ele.", "Usunąć „{0}” ({1}) z marketplace?\nGracze, którzy go zainstalowali, zachowają go.", "Удалить «{0}» ({1}) из маркетплейса?\nУ тех, кто его установил, он останется.", "「{0}」（{1}）をマーケットプレイスから削除しますか？\nインストール済みのプレイヤーはそのまま使えます。", "「{0}」({1})을(를) 마켓플레이스에서 삭제할까요?\n설치한 플레이어는 계속 사용할 수 있습니다.", "要从市场移除「{0}」（{1}）吗？\n已安装的玩家会保留它。" },
            ["remove_failed"] = new[] { "Couldn't remove: {0}", "Impossible de retirer : {0}", "Entfernen fehlgeschlagen: {0}", "Impossibile rimuovere: {0}", "No se pudo retirar: {0}", "Não foi possível remover: {0}", "Nie udało się usunąć: {0}", "Не удалось удалить: {0}", "削除できません：{0}", "삭제 실패: {0}", "无法移除：{0}" },
            ["mine_help"] = new[]
            {
                "To publish: pick a song, “Publish”, then give the link of its YouTube video. Only the chart is sent, never the audio.",
                "Pour publier : choisis un morceau, « Publier », puis donne le lien de sa vidéo YouTube. Seul le chart est envoyé, jamais l'audio.",
                "Zum Veröffentlichen: Song wählen, „Veröffentlichen“, dann den Link seines YouTube-Videos angeben. Nur der Chart wird gesendet, nie das Audio.",
                "Per pubblicare: scegli un brano, «Pubblica», poi indica il link del suo video YouTube. Viene inviato solo il chart, mai l'audio.",
                "Para publicar: elige una canción, «Publicar» y luego el enlace de su vídeo de YouTube. Solo se envía el chart, nunca el audio.",
                "Para publicar: escolha uma música, “Publicar” e depois o link do vídeo no YouTube. Só o chart é enviado, nunca o áudio.",
                "Aby opublikować: wybierz utwór, „Opublikuj”, a potem podaj link do filmu na YouTube. Wysyłany jest tylko chart, nigdy audio.",
                "Чтобы опубликовать: выберите трек, «Опубликовать», затем укажите ссылку на его видео YouTube. Отправляется только чарт, без аудио.",
                "公開するには：曲を選び「公開」を押して、YouTube動画のリンクを入力します。送信されるのはチャートだけで、音源は送られません。",
                "공개하려면: 곡을 고르고 「공개」를 누른 뒤 YouTube 영상 링크를 입력하세요. 차트만 전송되며 음원은 보내지 않습니다.",
                "发布方法：选择一首歌，点击「发布」，然后填写其 YouTube 视频链接。只发送谱面，不发送音频。",
            },
            ["publish"] = new[] { "PUBLISH", "PUBLIER", "VERÖFFENTLICHEN", "PUBBLICA", "PUBLICAR", "PUBLICAR", "OPUBLIKUJ", "ОПУБЛИКОВАТЬ", "公開", "공개", "发布" },
            ["update"] = new[] { "UPDATE", "METTRE À JOUR", "AKTUALISIEREN", "AGGIORNA", "ACTUALIZAR", "ATUALIZAR", "AKTUALIZUJ", "ОБНОВИТЬ", "更新", "업데이트", "更新" },
            ["withdraw"] = new[] { "UNPUBLISH", "RETIRER LA PUBLICATION", "VERÖFFENTLICHUNG ZURÜCKZIEHEN", "RITIRA LA PUBBLICAZIONE", "RETIRAR PUBLICACIÓN", "DESPUBLICAR", "WYCOFAJ PUBLIKACJĘ", "СНЯТЬ С ПУБЛИКАЦИИ", "公開を取り消す", "공개 취소", "取消发布" },
            ["no_songs"] = new[] { "No custom song ready. Drop mp3 files into the CustomSongs folder.", "Aucun morceau custom prêt. Dépose des mp3 dans le dossier CustomSongs.", "Kein Custom-Song bereit. Leg MP3-Dateien in den Ordner CustomSongs.", "Nessun brano custom pronto. Metti degli mp3 nella cartella CustomSongs.", "Ninguna canción custom lista. Pon archivos mp3 en la carpeta CustomSongs.", "Nenhuma música custom pronta. Coloque arquivos mp3 na pasta CustomSongs.", "Brak gotowych utworów. Wrzuć pliki mp3 do folderu CustomSongs.", "Нет готовых треков. Положите mp3 в папку CustomSongs.", "準備できたカスタム曲がありません。CustomSongsフォルダーにmp3を入れてください。", "준비된 커스텀 곡이 없습니다. CustomSongs 폴더에 mp3를 넣으세요.", "没有可用的自定义歌曲。请将 mp3 放入 CustomSongs 文件夹。" },
            ["published"] = new[] { "PUBLISHED", "PUBLIÉ", "VERÖFFENTLICHT", "PUBBLICATO", "PUBLICADO", "PUBLICADO", "OPUBLIKOWANO", "ОПУБЛИКОВАН", "公開中", "공개됨", "已发布" },
            ["from_marketplace"] = new[] { "MARKETPLACE", "MARKETPLACE", "MARKETPLACE", "MARKETPLACE", "MARKETPLACE", "MARKETPLACE", "MARKETPLACE", "МАРКЕТПЛЕЙС", "マーケット", "마켓", "市场" },
            ["installed_from"] = new[] { "Chart by {0}, installed from the marketplace.", "Chart de {0}, installé depuis le marketplace.", "Chart von {0}, aus dem Marketplace installiert.", "Chart di {0}, installato dal marketplace.", "Chart de {0}, instalado desde el marketplace.", "Chart de {0}, instalado do marketplace.", "Chart: {0}, zainstalowany z marketplace.", "Чарт: {0}, установлен из маркетплейса.", "{0} のチャート（マーケットプレイスからインストール）。", "{0}의 차트, 마켓플레이스에서 설치됨.", "{0} 的谱面，从市场安装。" },
            ["published_long"] = new[] { "Published on the marketplace.", "Publié sur le marketplace.", "Im Marketplace veröffentlicht.", "Pubblicato sul marketplace.", "Publicado en el marketplace.", "Publicado no marketplace.", "Opublikowano w marketplace.", "Опубликован в маркетплейсе.", "マーケットプレイスで公開中。", "마켓플레이스에 공개됨.", "已在市场发布。" },
            ["not_published"] = new[] { "Not published yet.", "Pas encore publié.", "Noch nicht veröffentlicht.", "Non ancora pubblicato.", "Aún no publicado.", "Ainda não publicado.", "Jeszcze nie opublikowano.", "Ещё не опубликован.", "未公開。", "아직 공개되지 않음.", "尚未发布。" },
            ["link_question"] = new[] { "YouTube video link for “{0}”", "Lien de la vidéo YouTube de « {0} »", "YouTube-Link zu „{0}“", "Link del video YouTube di «{0}»", "Enlace del vídeo de YouTube de «{0}»", "Link do vídeo do YouTube de “{0}”", "Link do filmu YouTube dla „{0}”", "Ссылка на видео YouTube для «{0}»", "「{0}」のYouTube動画リンク", "「{0}」의 YouTube 영상 링크", "「{0}」的 YouTube 视频链接" },
            ["link_hint"] = new[]
            {
                "Type or paste (Ctrl+V) the link of the song's video. Only the chart is sent, never the audio.",
                "Tape ou colle (Ctrl+V) le lien de la vidéo du morceau. Seul le chart est envoyé, jamais l'audio.",
                "Link des Videos eingeben oder einfügen (Strg+V). Nur der Chart wird gesendet, nie das Audio.",
                "Scrivi o incolla (Ctrl+V) il link del video del brano. Viene inviato solo il chart, mai l'audio.",
                "Escribe o pega (Ctrl+V) el enlace del vídeo de la canción. Solo se envía el chart, nunca el audio.",
                "Digite ou cole (Ctrl+V) o link do vídeo da música. Só o chart é enviado, nunca o áudio.",
                "Wpisz lub wklej (Ctrl+V) link do filmu. Wysyłany jest tylko chart, nigdy audio.",
                "Введите или вставьте (Ctrl+V) ссылку на видео. Отправляется только чарт, без аудио.",
                "曲の動画リンクを入力または貼り付け（Ctrl+V）。送信されるのはチャートだけです。",
                "곡 영상 링크를 입력하거나 붙여넣으세요(Ctrl+V). 차트만 전송됩니다.",
                "输入或粘贴（Ctrl+V）歌曲视频链接。只发送谱面，不发送音频。",
            },
            ["link_invalid"] = new[] { "That's not a YouTube video link (youtube.com/watch?v=… or youtu.be/…).", "Ce n'est pas un lien de vidéo YouTube (youtube.com/watch?v=… ou youtu.be/…).", "Das ist kein YouTube-Videolink (youtube.com/watch?v=… oder youtu.be/…).", "Non è un link a un video YouTube (youtube.com/watch?v=… o youtu.be/…).", "No es un enlace de vídeo de YouTube (youtube.com/watch?v=… o youtu.be/…).", "Isso não é um link de vídeo do YouTube (youtube.com/watch?v=… ou youtu.be/…).", "To nie jest link do filmu YouTube (youtube.com/watch?v=… lub youtu.be/…).", "Это не ссылка на видео YouTube (youtube.com/watch?v=… или youtu.be/…).", "YouTube動画のリンクではありません（youtube.com/watch?v=… または youtu.be/…）。", "YouTube 영상 링크가 아닙니다 (youtube.com/watch?v=… 또는 youtu.be/…).", "这不是 YouTube 视频链接（youtube.com/watch?v=… 或 youtu.be/…）。" },
            ["fingerprinting"] = new[] { "Analysing the audio (fingerprint)…", "Analyse de l'audio (empreinte)…", "Audio wird analysiert (Fingerabdruck)…", "Analisi dell'audio (impronta)…", "Analizando el audio (huella)…", "Analisando o áudio (impressão)…", "Analiza audio (odcisk)…", "Анализ аудио (отпечаток)…", "音声を解析中（フィンガープリント）…", "음원 분석 중(지문)…", "正在分析音频（指纹）…" },
            ["sending"] = new[] { "Sending…", "Envoi…", "Wird gesendet…", "Invio…", "Enviando…", "Enviando…", "Wysyłanie…", "Отправка…", "送信中…", "전송 중…", "发送中…" },
            ["updated"] = new[] { "Updated (version {0}).", "Mis à jour (version {0}).", "Aktualisiert (Version {0}).", "Aggiornato (versione {0}).", "Actualizado (versión {0}).", "Atualizado (versão {0}).", "Zaktualizowano (wersja {0}).", "Обновлено (версия {0}).", "更新しました（バージョン {0}）。", "업데이트됨 (버전 {0}).", "已更新（版本 {0}）。" },
            ["published_done"] = new[] { "Published! Other players can now install it.", "Publié ! Les autres joueurs peuvent maintenant l'installer.", "Veröffentlicht! Andere Spieler können ihn jetzt installieren.", "Pubblicato! Ora gli altri giocatori possono installarlo.", "¡Publicado! Los demás jugadores ya pueden instalarlo.", "Publicado! Outros jogadores já podem instalá-lo.", "Opublikowano! Inni gracze mogą go teraz zainstalować.", "Опубликовано! Другие игроки теперь могут его установить.", "公開しました！他のプレイヤーがインストールできます。", "공개 완료! 이제 다른 플레이어가 설치할 수 있습니다.", "已发布！其他玩家现在可以安装了。" },
            ["publish_failed"] = new[] { "Couldn't publish: {0}", "Publication impossible : {0}", "Veröffentlichen fehlgeschlagen: {0}", "Pubblicazione non riuscita: {0}", "No se pudo publicar: {0}", "Não foi possível publicar: {0}", "Nie udało się opublikować: {0}", "Не удалось опубликовать: {0}", "公開できません：{0}", "공개 실패: {0}", "无法发布：{0}" },
            ["withdraw_confirm"] = new[] { "Remove “{0}” from the marketplace?\nPlayers who installed it keep it.", "Retirer « {0} » du marketplace ?\nLes joueurs qui l'ont installé le gardent.", "„{0}“ aus dem Marketplace entfernen?\nWer ihn installiert hat, behält ihn.", "Rimuovere «{0}» dal marketplace?\nChi l'ha installato lo conserva.", "¿Retirar «{0}» del marketplace?\nQuien lo instaló lo conserva.", "Remover “{0}” do marketplace?\nQuem instalou continua com ele.", "Usunąć „{0}” z marketplace?\nGracze, którzy go zainstalowali, zachowają go.", "Удалить «{0}» из маркетплейса?\nУ тех, кто его установил, он останется.", "「{0}」をマーケットプレイスから削除しますか？\nインストール済みのプレイヤーはそのまま使えます。", "「{0}」을(를) 마켓플레이스에서 삭제할까요?\n설치한 플레이어는 계속 사용할 수 있습니다.", "要从市场移除「{0}」吗？\n已安装的玩家会保留它。" },
            ["withdrawn"] = new[] { "Removed from the marketplace.", "Retiré du marketplace.", "Aus dem Marketplace entfernt.", "Rimosso dal marketplace.", "Retirado del marketplace.", "Removido do marketplace.", "Usunięto z marketplace.", "Удалено из маркетплейса.", "マーケットプレイスから削除しました。", "마켓플레이스에서 삭제됨.", "已从市场移除。" },
        };
    }
}
