// The third-party art the game builds from, made by Graveway → Set up
// project (Editor/ArtImport.cs) at Resources/Art/Catalog.asset, so a build
// takes only what is listed here:
//   - characters: each model with an Animator controller holding its clips
//     (one state per clip, named as the clip without its "CharacterArmature|")
//   - guns: the kit's own guns, posed as a survivor's hand holds them, with
//     where parts go on each
//   - parts: gun accessories, each set to attach at its own origin
//   - props: static models (vehicles, barriers, lamps, litter), with their
//     outline and where their light is
//   - hats: lifted off the Ultimate Modular characters' heads, with where
//     each sits on each survivor
// and the textures they share.

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

        // A mesh and where it sits, in the space of whatever it's fitted to.
        [Serializable]
        public sealed class Placed
        {
            public string id;
            public Mesh mesh;
            public Color[] colors;           // one per submesh; empty: it takes the kit's palette
            public Vector3 position, scale = Vector3.one;
            public Quaternion rotation = Quaternion.identity;
        }

        // A gun in the gun mount's space (grip at the origin, barrel along
        // +z, metres), and the points parts attach to: the muzzle, the top of
        // the receiver, under the handguard, the back end, and the bottom of
        // the magazine well.
        [Serializable]
        public sealed class Gun
        {
            public Placed body;
            public Vector3 muzzle, sight, rail, stock, mag;
            public float width, length;
            public Bounds bounds;
        }

        // A static model, measured in its own space: its outline, and the
        // middle of its light (a lamp's glass, a car's headlights), if any.
        [Serializable]
        public sealed class Prop
        {
            public string id;
            public GameObject model;
            public Bounds bounds;
            public Vector3 light;
        }

        // A hat: its mesh (sitting on the origin, the top of the head it came
        // off, in that head's metres), a colour per submesh, and where it
        // goes on each survivor model, in their Head bone's space (id: the
        // model's).
        [Serializable]
        public sealed class Hat
        {
            public string id;
            public Mesh mesh;
            public Color[] colors;
            public Placed[] on = Array.Empty<Placed>();
        }

        public Entry[] models = Array.Empty<Entry>();
        public Prop[] props = Array.Empty<Prop>();
        public Gun[] guns = Array.Empty<Gun>();
        public Placed[] parts = Array.Empty<Placed>();
        public Hat[] hats = Array.Empty<Hat>();
        public Texture2D zombieAtlas;        // the Zombie Apocalypse Kit's colour palette

        static ArtCatalog loaded;
        public static ArtCatalog Get => loaded ? loaded : (loaded = Resources.Load<ArtCatalog>("Art/Catalog"));

        public Entry Find(string id) => Array.Find(models, e => e.id == id);
        public Gun FindGun(string id) => Array.Find(guns, g => g.body.id == id);
        public Prop FindProp(string id) => Array.Find(props, p => p.id == id);
        public Placed FindPart(string id) => Array.Find(parts, p => p.id == id);
        public Hat FindHat(string id) => Array.Find(hats, h => h.id == id);
    }
}
