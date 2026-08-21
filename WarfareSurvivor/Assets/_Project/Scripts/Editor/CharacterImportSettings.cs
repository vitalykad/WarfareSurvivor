using System.IO;
using UnityEditor;
using UnityEngine;

namespace WarfareSurvivor.EditorTools
{
    /// <summary>
    /// Ставит риг и настройки клипов автоматически, по расположению файла.
    ///
    /// Зачем постпроцессор, а не руки: модели и анимации приходят из разных
    /// источников (модели — Tripo, анимации — Mixamo) и риги у них РАЗНЫЕ.
    /// Связать их можно только через Humanoid — Unity нормализует обе скелетные
    /// иерархии к своему аватару и ретаргетит клип. Если хоть один файл окажется
    /// Generic, анимация на модель не ляжет вообще, а сообщение об этом будет
    /// невнятным. Поэтому Humanoid ставится кодом, а не галочкой в инспекторе:
    /// новый файл в Models/ или Animations/ настраивается сам.
    ///
    /// Трогаем ТОЛЬКО поля рига и клипов. Материалы, масштаб, компрессия и всё
    /// остальное остаются как настроено руками.
    /// </summary>
    public class CharacterImportSettings : AssetPostprocessor
    {
        const string ModelsFolder = "/Models/";
        const string AnimationsFolder = "/Animations/";

        // Поднять число, чтобы Unity переимпортировала уже лежащие в проекте FBX
        // с новыми настройками. Без этого правки ниже подействуют только на
        // файлы, добавленные после правки.
        public override uint GetVersion() => 4;

        bool IsModel => assetPath.Contains(ModelsFolder);
        bool IsAnimation => assetPath.Contains(AnimationsFolder);

        void OnPreprocessModel()
        {
            if (!(assetImporter is ModelImporter importer)) return;
            if (!IsModel && !IsAnimation) return;

            // Humanoid ставим только тому, у кого есть скелет. Статичной
            // модели — руинам, ящику, забору — он не нужен, и попытка собрать
            // из неё аватар валит импорт сообщением про отсутствующую кость
            // Hips, которое никак не намекает на настоящую причину.
            if (IsModel && !HasSkinning(assetPath))
            {
                importer.animationType = ModelImporterAnimationType.None;
                importer.importAnimation = false;
                return;
            }

            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;

            if (IsModel)
            {
                // В модели анимаций нет — не плодим пустые клипы.
                importer.importAnimation = false;
            }
            else
            {
                // В файле анимации меш и материалы не нужны: из него берётся
                // только клип, а рисуется всё равно модель.
                importer.importAnimation = true;
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                importer.importCameras = false;
                importer.importLights = false;
            }
        }

        void OnPreprocessAnimation()
        {
            if (!(assetImporter is ModelImporter importer)) return;
            if (!IsAnimation) return;

            var defaults = importer.defaultClipAnimations;
            if (defaults.Length == 0) return;

            var clip = defaults[0];
            clip.name = ClipNameFromFile(assetPath);

            // Зацикливается только то, что играется непрерывно. Разовые клипы
            // перечислены явно: удар, зациклившийся сам собой, выглядит как
            // безостановочное махание лопатой и не даёт поймать момент попадания.
            clip.loopTime = !IsOneShot(clip.name);

            // Root motion выключаем запеканием в позу. Ходьбу задаёт код: если
            // оставить смещение в клипе, бойцы поедут сами и разъедутся со
            // своими слотами в строю.
            clip.lockRootRotation = true;
            clip.lockRootHeightY = true;
            clip.lockRootPositionXZ = true;

            importer.clipAnimations = new[] { clip };
        }

        /// <summary>
        /// Есть ли в FBX скиннинг. Проверяем по содержимому, а не по папке:
        /// договорённость об именовании папок держится ровно до того дня,
        /// когда о ней забудут, и тогда импорт снова свалится с невнятным
        /// сообщением. Имена узлов лежат в файле сырыми строками и в текстовом
        /// FBX, и в бинарном, поэтому хватает поиска подстроки.
        /// </summary>
        static bool HasSkinning(string path)
        {
            const string Marker = "Deformer";

            try
            {
                using (var stream = File.OpenRead(path))
                {
                    var buffer = new byte[64 * 1024];
                    var marker = System.Text.Encoding.ASCII.GetBytes(Marker);
                    int overlap = marker.Length - 1;
                    int carried = 0;

                    int read;
                    while ((read = stream.Read(buffer, carried, buffer.Length - carried)) > 0)
                    {
                        int total = carried + read;
                        if (IndexOf(buffer, total, marker) >= 0) return true;

                        // Хвост переносим в начало: маркер может лечь на стык блоков.
                        carried = Mathf.Min(overlap, total);
                        System.Array.Copy(buffer, total - carried, buffer, 0, carried);
                    }
                }
            }
            catch (IOException e)
            {
                // Не смогли прочитать — считаем, что скелет есть: прежнее
                // поведение для персонажей важнее, чем для окружения.
                Debug.LogWarning($"[CharacterImportSettings] Не прочитан {path}: {e.Message}");
                return true;
            }

            return false;
        }

        static int IndexOf(byte[] buffer, int length, byte[] pattern)
        {
            for (int i = 0; i <= length - pattern.Length; i++)
            {
                int j = 0;
                while (j < pattern.Length && buffer[i + j] == pattern[j]) j++;
                if (j == pattern.Length) return i;
            }
            return -1;
        }

        static readonly string[] OneShotMarkers = { "Attack", "Dying", "Death", "Hit", "Die" };

        static bool IsOneShot(string clipName)
        {
            foreach (var marker in OneShotMarkers)
                if (clipName.IndexOf(marker, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }

        /// <summary>"X Bot@Pistol Idle.fbx" -> "Pistol Idle".</summary>
        static string ClipNameFromFile(string path)
        {
            var file = Path.GetFileNameWithoutExtension(path);
            var at = file.IndexOf('@');
            return at >= 0 ? file.Substring(at + 1) : file;
        }
    }
}
