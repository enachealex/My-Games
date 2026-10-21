// The third-party art the game builds its characters from: each model with
// an Animator controller holding its clips (one state per clip, named as the
// clip without its "CharacterArmature|"), and the textures they share. Made
// by Graveway → Set up project (Editor/ArtImport.cs) at
// Resources/Art/Catalog.asset, so a build takes only what is listed here.

using System;
using UnityEngine;

namespace Graveway
{
    public sealed class ArtCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public string id;
            public GameObject model;
            public RuntimeAnimatorController controller;
        }

        public Entry[] models = Array.Empty<Entry>();
        public Texture2D zombieAtlas;        // the Zombie Apocalypse Kit's colour palette

        static ArtCatalog loaded;
        public static ArtCatalog Get => loaded ? loaded : (loaded = Resources.Load<ArtCatalog>("Art/Catalog"));

        public Entry Find(string id) => Array.Find(models, e => e.id == id);
    }
}
