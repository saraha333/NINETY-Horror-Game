#  NINETY — Horror Escape Room

> *"Every time the Kidnapper catches you, you don’t lose the game. You lose part of your memory."*

![Unity](https://img.shields.io/badge/Engine-Unity%206%20%28URP%29-blue?style=for-the-badge&logo=unity)
![Genre](https://img.shields.io/badge/Genre-Psychological%20Horror%20%2F%20Puzzle-red?style=for-the-badge)
![Platform](https://img.shields.io/badge/Platform-Windows%20PC%20%7C%20WebGL-lightgrey?style=for-the-badge)

---

## 🪷 Overview & Narrative
**NINETY** is a first-person 3D psychological horror escape-room game built in Unity (URP). 

The player wakes up chained to an iron bed inside a sealed room with a diegetic digital timer on the wall counting down from **90 seconds**. To escape, you must search the room, collect physical clues, and decipher numerical security locks before the timer expires or the stalker entity captures you.

### 🧠 The Memory Integrity Twist
Failure isn't game over[cite: 1]. Every time you are captured, your **Memory Integrity** decreases, modifying the room, altering environmental clues (such as tally marks, radio recordings, and code sequences), and shifting the monster's behavior across attempts[cite: 1].

---

## ✨ Key Features
* **⏳ 90-Second Loop:** High-pressure decision-making driven by a wall-mounted digital timer[cite: 1].
* **🧩 Dynamic Clues & Puzzles:** Intersecting clues involving radio audio recordings, room notes, and wall tallies that change dynamically across capture cycles[cite: 1].
* **👁️ Adaptive Entity Threat:** A stalker entity with vision and hearing cones that reacts to player movement, noise, and sightlines[cite: 1].
* **🚪 4 Branching Endings:** Good Ending, Altered Escapes, True Ending, and Fade Ending dictated by Memory Integrity and uncovered secrets[cite: 1].

---
## 👥 Development Team
* **Sarah**
* **Shahad**
---

## 🕹️ Controls Scheme

| Input | Action | Description |
| :--- | :--- | :--- |
| **W, A, S, D** | Movement | Walk through the room[cite: 1] |
| **Mouse / Cursor** | Look Around | First-person camera control[cite: 1] |
| **E** | Interact | Inspect items, press keypad buttons, play radio[cite: 1] |
| **Esc / P** | Pause | Toggle pause menu and settings |

---

## 🏗️ Technical Architecture & Directory Structure

### 🛠️ Tech Stack & Specifications
| Layer / System | Technology / Configuration | Description |
| :--- | :--- | :--- |
| **Game Engine** | Unity 6 (URP)[cite: 1] | Universal Render Pipeline for high-fidelity horror lighting |
| **Programming Language** | C# | Core game logic, state machine, and interactive systems |
| **Target Platforms** | PC / WebGL | Desktop executable and web-browser playable build[cite: 1] |
| **Version Control Setup** | Git & GitHub | Force Text Serialization & Visible `.meta` files |

### 📂 Directory Structure
```text
NINETY-Horror-Game/
│
├── 📂 Assets/                        # Core game assets & resources
│   ├── 📂 Animations/                # Character, door, and prop animations
│   ├── 📂 Audio/                     # Sound effects and radio recordings
│   │   ├── 📂 BGM/                   # Atmospheric ambient tracks
│   │   ├── 📂 SFX/                   # Environmental horror SFX
│   │   └── 📂 VoiceOvers/            # Radio voice logs (Ethan's recordings)
│   ├── 📂 Materials/                 # Universal Render Pipeline (URP) materials
│   ├── 📂 Models/                    # 3D models and environment props
│   │   ├── 📂 Environment/           # Room geometry, iron bed, and furniture
│   │   └── 📂 Interactables/         # Radio, keypad, box, keys, and notes
│   ├── 📂 Prefabs/                   # Reusable functional prefabs
│   │   ├── 📂 UI/                    # Digital timer HUD and UI components
│   │   └── 📂 Gameplay/              # Lighting, player controller, and enemy
│   ├── 📂 Scenes/                    # Unity scene files
│   │   ├── MainMenu.unity            # Main menu interface
│   │   └── EscapeRoom.unity          # Core escape room gameplay scene
│   ├── 📂 Scripts/                   # C# Source Code
│   │   ├── 📂 Core/                  # GameManager, Timer System, Memory System
│   │   ├── 📂 Player/                # First-person movement and raycast interaction
│   │   ├── 📂 Puzzles/               # Keypad logic, lock states, radio puzzle
│   │   └── 📂 UI/                    # HUD managers, menus, and ending screens
│   └── 📂 Textures/                  # Textures and surface maps
│
├── 📂 Packages/                      # Unity package dependencies
├── 📂 ProjectSettings/               # Physics, Input System, and URP settings
│
├── .gitignore                        # Git ignore rules for Unity temporary files
├── LICENSE                           # Project license
└── README.md                         # Project documentation
