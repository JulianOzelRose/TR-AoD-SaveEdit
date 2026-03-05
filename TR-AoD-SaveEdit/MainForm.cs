using ICSharpCode.SharpZipLib.Zip.Compression.Streams;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace TR_AoD_SaveEdit
{
    public partial class MainForm : Form
    {
        public MainForm()
        {
            InitializeComponent();
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            CheckConfigFile();
        }

        private void MainForm_Shown(object sender, EventArgs e)
        {
            this.Refresh(); // Paint UI first

            LoadActorDB();

            if (!string.IsNullOrEmpty(savegameDirectory))
            {
                LoadSavegameList();
            }
        }

        // Static offsets & constants
        private const int HEADER_SIZE = 0x124;
        private const int HEADER_DAY_OFFSET = 0x10;
        private const int HEADER_MONTH_OFFSET = 0x0C;
        private const int HEADER_YEAR_OFFSET = 0x0A;
        private const int HEADER_HOUR_OFFSET = 0x12;
        private const int HEADER_MINUTE_OFFSET = 0x14;
        private const int HEADER_SECOND_OFFSET = 0x16;
        private const int COMPRESSED_BLOCK_MAX_SIZE = 0xFFFFFF;
        private const int PAYLOAD_SIZE = 0x32000;
        private const int COMPRESSED_SIZE_FIELD_IN_PAYLOAD = 0x7FFC;
        private const int HEADER_LEVEL_INDEX_OFFSET = 0x08;
        private const int COMPRESSED_BLOCK_SIZE_OFFSET = 0x8120;
        private const int PLAYER_CASH_OFFSET = 0x11;
        private static readonly byte[] TOMB_SIGNATURE = new byte[] { 0x54, 0x4F, 0x4D, 0x42 };

        // Dynamic offsets
        private int INVENTORY_START_OFFSET;
        private int POST_INVENTORY_END_OFFSET;
        private int INVENTORY_END_OFFSET;
        private int PLAYER_HEALTH_OFFSET;
        private int PLAYER_HEALTH_OFFSET_2;

        // Inventory types
        private const int INVENTORY_TYPE_WEAPON = 3;
        private const int INVENTORY_TYPE_ITEM = 4;
        private const int INVENTORY_TYPE_AMMO = 7;

        // Inventory arrays
        private List<InventoryItem> invLara = new List<InventoryItem>();
        private List<InventoryItem> invKurtis = new List<InventoryItem>();

        // Globals & game vars
        private ushort sgCurrentLevel;
        private Int32 sgCurrentLoadedZone;
        private string mapName;

        // Savegame vars
        private string savegameDirectory;
        private string gameDirectory;
        private string currentSavegamePath;
        private List<Savegame> savegames = new List<Savegame>();

        // Savegame buffer
        private List<byte> decompressedBlock;
        private List<byte> remainingBlock;
        private byte[] postInventoryStructuredBlock;
        private int sgBufferCursor = 0;

        // Entity counts
        private int NUM_ACTORS = 0;
        private int NUM_OBJECTS = 0;
        private int NUM_TRIGGERS = 0;
        private int NUM_EMITTERS = 0;
        private int NUM_AUDIO_LOCATORS = 0;
        private int NUM_ROOMS = 0;

        // Entity mocks
        private List<EntityMock> actors = new List<EntityMock>();
        private List<EntityMock> objects = new List<EntityMock>();
        private List<EntityMock> rooms = new List<EntityMock>();
        private List<GmxObjectInfo> gmxObjects = new List<GmxObjectInfo>();

        // Actors & Entities
        private Dictionary<int, Actor> ActorDB = new Dictionary<int, Actor>();
        private Dictionary<string, List<EntityMock>> entityMocks = new Dictionary<string, List<EntityMock>>();
        private List<byte> decompressedGMX = new List<byte>();

        // Misc
        private const string CONFIG_FILE_NAME = "TR-AoD-SaveEdit.ini";
        private bool isInventoryLoading = false;
        private bool isLoading = false;

        // Actor DB
        public class Actor
        {
            public int ID { get; set; }
            public string Name { get; set; }
            public bool IsPlayable { get; set; }

            public Actor(int id, string name, bool isPlayable)
            {
                ID = id;
                Name = name;
                IsPlayable = isPlayable;
            }
        }

        // Level names
        private readonly Dictionary<byte, string> levelNames = new Dictionary<byte, string>()
        {
            {  0, "Parisian Back Streets"       },
            {  1, "Derelict Apartment Block"    },
            {  2, "Margot Carvier's Apartment"  },
            {  3, "Industrial Roof Tops"        },
            {  4, "Parisian Ghetto"             },
            {  5, "Parisian Ghetto"             },
            {  6, "Parisian Ghetto"             },
            {  7, "The Serpent Rouge"           },
            {  8, "Rennes' Pawnshop"            },
            {  9, "Willowtree Herbalist"        },
            { 10, "St. Aicard's Church"         },
            { 11, "Café Metro"                  },
            { 12, "St. Aicard's Graveyard"      },
            { 13, "Bouchard's Hideout"          },
            { 14, "Louvre Storm Drains"         },
            { 15, "Louvre Galleries"            },
            { 16, "Galleries Under Siege"       },
            { 17, "Tomb of Ancients"            },
            { 18, "The Archaeological Dig"      },
            { 19, "Von Croy's Apartment"        },
            { 20, "The Monstrum Crimescene"     },
            { 21, "The Strahov Fortress"        },
            { 22, "The Bio-Research Facility"   },
            { 23, "Aquatic Research Area"       },
            { 24, "The Sanitarium"              },
            { 25, "Maximum Containment Area"    },
            { 26, "The Vault of Trophies"       },
            { 27, "Boaz Returns"                },
            { 28, "Eckhardt's Lab"              },
            { 29, "The Lost Domain"             },
            { 30, "The Hall of Seasons"         },
            { 31, "Neptune's Hall"              },
            { 32, "Wrath of the Beast"          },
            { 33, "The Sanctuary of Flame"      },
            { 34, "The Breath of Hades"         }
        };

        private readonly Dictionary<byte, string> mapNames = new Dictionary<byte, string>()
        {
            {  0, "PARIS1"                      },  // Parisian Back Streets
            {  1, "PARIS1A"                     },  // Derelict Apartment Block
            {  2, "PARIS1B"                     },  // Margot Carvier's Apartment
            {  3, "PARIS1C"                     },  // Industrial Roof Tops
            {  4, "PARIS2_1"                    },  // Parisian Ghetto
            {  5, "PARIS2_2"                    },  // Parisian Ghetto
            {  6, "PARIS2_3"                    },  // Parisian Ghetto
            {  7, "PARIS2B"                     },  // The Serpent Rouge
            {  8, "PARIS2C"                     },  // Rennes' Pawnshop
            {  9, "PARIS2D"                     },  // Willowtree Herbalist
            { 10, "PARIS2E"                     },  // St. Aicard's Church
            { 11, "PARIS2F"                     },  // Café Metro
            { 12, "PARIS2G"                     },  // St. Aicard's Graveyard
            { 13, "PARIS2H"                     },  // Bouchard's Hideout
            { 14, "PARIS3"                      },  // Louvre Storm Drains
            { 15, "PARIS4"                      },  // Louvre Galleries
            { 16, "PARIS4A"                     },  // Galleries Under Siege
            { 17, "PARIS5"                      },  // Tomb of Ancients
            { 18, "PARIS5A"                     },  // The Archaeological Dig
            { 19, "PARIS6"                      },  // Von Croy's Apartment
            { 20, "PRAGUE1"                     },  // The Monstrum Crimescene
            { 21, "PRAGUE2"                     },  // The Strahov Fortress
            { 22, "PRAGUE3"                     },  // The Bio-Research Facility
            { 23, "PRAGUE3A"                    },  // Aquatic Research Area
            { 24, "PRAGUE4"                     },  // The Sanitarium
            { 25, "PRAGUE4A"                    },  // Maximum Containment Area
            { 26, "PRAGUE5"                     },  // The Vault of Trophies
            { 27, "PRAGUE5A"                    },  // Boaz Returns
            { 28, "PRAGUE6"                     },  // Eckhardt's Lab
            { 29, "PRAGUE6A"                    },  // The Lost Domain
            { 30, "PARIS5_1"                    },  // The Hall of Seasons
            { 31, "PARIS5_2"                    },  // Neptune's Hall
            { 32, "PARIS5_3"                    },  // Wrath of the Beast
            { 33, "PARIS5_4"                    },  // The Sanctuary of Flame
            { 34, "PARIS5_5"                    },  // The Breath of Hades
        };

        private void LoadSavegameList()
        {
            cmbSavegame.SelectedIndexChanged -= cmbSavegame_SelectedIndexChanged;

            PopulateSavegames();

            if (cmbSavegame.Items.Count > 0)
            {
                cmbSavegame.SelectedIndex = 0;
                cmbSavegame_SelectedIndexChanged(this, EventArgs.Empty);
            }

            cmbSavegame.SelectedIndexChanged += cmbSavegame_SelectedIndexChanged;
        }

        private void LoadActorDB()
        {
            string actorPath = $"{gameDirectory}\\data\\ACTOR.db";

            if (!string.IsNullOrEmpty(gameDirectory) && Directory.Exists(gameDirectory) && File.Exists(actorPath))
            {
                ParseActorDB(actorPath);
            }
        }

        private void ResetInventoryDisplay()
        {
            // Determine whose inventory to update
            List<InventoryItem> selectedInventory = cmbInventory.SelectedIndex == 1 ? invKurtis : invLara;

            // Copy list to safely iterate without modifying it
            List<InventoryItem> inventoryCopy = selectedInventory.ToList();

            // Reset UI fields
            nudChocolateBar.Value = 0;
            nudSmallMedipack.Value = 0;
            nudHealthBandages.Value = 0;
            nudLargeHealthPack.Value = 0;
            nudHealthPills.Value = 0;
            nudPoisonAntidote.Value = 0;
            chkMV9.Checked = false;
            chkVPacker.Checked = false;
            nudMV9Ammo.Value = 0;
            nudVPackerAmmo.Value = 0;
            chkBoranX.Checked = false;
            nudBoranXAmmo.Value = 0;
            chkK2Impactor.Checked = false;
            nudK2ImpactorAmmo.Value = 0;
            chkScorpionX.Checked = false;
            chkScorpionXPair.Checked = false;
            nudScorpionXAmmo.Value = 0;
            chkVectorR35.Checked = false;
            chkVectorR35Pair.Checked = false;
            nudVectorR35Ammo.Value = 0;
            chkDesertRanger.Checked = false;
            nudDesertRangerAmmo.Value = 0;
            chkDartSS.Checked = false;
            nudDartSSAmmo.Value = 0;
            chkRigg09.Checked = false;
            nudRigg09Ammo.Value = 0;
            chkViperSMG.Checked = false;
            nudViperSMGAmmo.Value = 0;
            chkMagVega.Checked = false;
            nudMagVegaAmmo.Value = 0;

            // Conditionally enable weapons
            chkMV9.Enabled = cmbInventory.SelectedIndex == 0;
            nudMV9Ammo.Enabled = cmbInventory.SelectedIndex == 0;
            chkVPacker.Enabled = cmbInventory.SelectedIndex == 0;
            nudVPackerAmmo.Enabled = cmbInventory.SelectedIndex == 0;
            chkScorpionX.Enabled = cmbInventory.SelectedIndex == 0;
            chkScorpionXPair.Enabled = cmbInventory.SelectedIndex == 0;
            nudScorpionXAmmo.Enabled = cmbInventory.SelectedIndex == 0;
            chkK2Impactor.Enabled = cmbInventory.SelectedIndex == 0;
            nudK2ImpactorAmmo.Enabled = cmbInventory.SelectedIndex == 0;
            chkVectorR35.Enabled = cmbInventory.SelectedIndex == 0;
            chkVectorR35Pair.Enabled = cmbInventory.SelectedIndex == 0;
            nudVectorR35Ammo.Enabled = cmbInventory.SelectedIndex == 0;
            chkDesertRanger.Enabled = cmbInventory.SelectedIndex == 0;
            nudDesertRangerAmmo.Enabled = cmbInventory.SelectedIndex == 0;
            chkMagVega.Enabled = cmbInventory.SelectedIndex == 0;
            nudMagVegaAmmo.Enabled = cmbInventory.SelectedIndex == 0;
            chkDartSS.Enabled = cmbInventory.SelectedIndex == 0;
            nudDartSSAmmo.Enabled = cmbInventory.SelectedIndex == 0;
            chkRigg09.Enabled = cmbInventory.SelectedIndex == 0;
            nudRigg09Ammo.Enabled = cmbInventory.SelectedIndex == 0;
            chkViperSMG.Enabled = cmbInventory.SelectedIndex == 0;
            nudViperSMGAmmo.Enabled = cmbInventory.SelectedIndex == 0;
            nudBoranXAmmo.Enabled = cmbInventory.SelectedIndex == 1;
            chkBoranX.Enabled = cmbInventory.SelectedIndex == 1;

            // Update UI based on inventory contents
            foreach (var item in inventoryCopy)
            {
                switch (item.ClassId)
                {
                    case Inventory.CHOCOLATE_BAR:
                        nudChocolateBar.Value = item.Quantity;
                        break;
                    case Inventory.SMALL_MEDIPACK:
                        nudSmallMedipack.Value = item.Quantity;
                        break;
                    case Inventory.HEALTH_BANDAGES:
                        nudHealthBandages.Value = item.Quantity;
                        break;
                    case Inventory.HEALTH_PILLS:
                        nudHealthPills.Value = item.Quantity;
                        break;
                    case Inventory.LARGE_HEALTH_PACK:
                        nudLargeHealthPack.Value = item.Quantity;
                        break;
                    case Inventory.MV9:
                        chkMV9.Checked = true;
                        break;
                    case Inventory.MV9_AMMO:
                        nudMV9Ammo.Value = item.Quantity;
                        break;
                    case Inventory.VPACKER:
                        chkVPacker.Checked = true;
                        break;
                    case Inventory.VPACKER_AMMO:
                        nudVPackerAmmo.Value = item.Quantity;
                        break;
                    case Inventory.BORAN_X:
                        chkBoranX.Checked = true;
                        break;
                    case Inventory.K2_IMPACTOR:
                        chkK2Impactor.Checked = true;
                        break;
                    case Inventory.K2_IMPACTOR_AMMO:
                        nudK2ImpactorAmmo.Value = item.Quantity;
                        break;
                    case Inventory.BORAN_X_AMMO:
                        nudBoranXAmmo.Value = item.Quantity;
                        break;
                    case Inventory.SCORPION_X:
                        chkScorpionX.Checked = true;
                        break;
                    case Inventory.SCORPION_X_AMMO:
                        nudScorpionXAmmo.Value = item.Quantity;
                        break;
                    case Inventory.VECTOR_R35:
                        chkVectorR35.Checked = true;
                        break;
                    case Inventory.VECTOR_R35_AMMO:
                        nudVectorR35Ammo.Value = item.Quantity;
                        break;
                    case Inventory.DESERT_RANGER:
                        chkDesertRanger.Checked = true;
                        break;
                    case Inventory.DESERT_RANGER_AMMO:
                        nudDesertRangerAmmo.Value = item.Quantity;
                        break;
                    case Inventory.DART_SS:
                        chkDartSS.Checked = true;
                        break;
                    case Inventory.DART_SS_AMMO:
                        nudDartSSAmmo.Value = item.Quantity;
                        break;
                    case Inventory.RIGG_09:
                        chkRigg09.Checked = true;
                        break;
                    case Inventory.RIGG_09_AMMO:
                        nudRigg09Ammo.Value = item.Quantity;
                        break;
                    case Inventory.VIPER_SMG:
                        chkViperSMG.Checked = true;
                        break;
                    case Inventory.VIPER_SMG_AMMO:
                        nudViperSMGAmmo.Value = item.Quantity;
                        break;
                    case Inventory.MAG_VEGA:
                        chkMagVega.Checked = true;
                        break;
                    case Inventory.MAG_VEGA_AMMO:
                        nudMagVegaAmmo.Value = item.Quantity;
                        break;
                    case Inventory.SCORPION_X_PAIR:
                        chkScorpionXPair.Checked = true;
                        break;
                    case Inventory.VECTOR_R35_PAIR:
                        chkVectorR35Pair.Checked = true;
                        break;
                    case Inventory.POISON_ANTIDOTE:
                        nudPoisonAntidote.Value = item.Quantity;
                        break;
                }
            }

            Debug.WriteLine($"Inventory UI Updated for {(cmbInventory.SelectedIndex == 1 ? "Kurtis" : "Lara")}.");
        }

        private List<UInt16> GetHeaderTimestamp(FileStream fs)
        {
            List<UInt16> timestamp = new List<UInt16>();

            using (BinaryReader reader = new BinaryReader(fs))
            {
                reader.BaseStream.Seek(HEADER_DAY_OFFSET, SeekOrigin.Begin);
                timestamp.Add(reader.ReadUInt16());

                reader.BaseStream.Seek(HEADER_MONTH_OFFSET, SeekOrigin.Begin);
                timestamp.Add(reader.ReadUInt16());

                reader.BaseStream.Seek(HEADER_YEAR_OFFSET, SeekOrigin.Begin);
                timestamp.Add(reader.ReadUInt16());

                reader.BaseStream.Seek(HEADER_HOUR_OFFSET, SeekOrigin.Begin);
                timestamp.Add(reader.ReadUInt16());

                reader.BaseStream.Seek(HEADER_MINUTE_OFFSET, SeekOrigin.Begin);
                timestamp.Add(reader.ReadUInt16());

                reader.BaseStream.Seek(HEADER_SECOND_OFFSET, SeekOrigin.Begin);
                timestamp.Add(reader.ReadUInt16());
            }

            return timestamp;
        }

        private void PopulateSavegames()
        {
            if (!string.IsNullOrEmpty(savegameDirectory) && Directory.Exists(savegameDirectory)
                && !string.IsNullOrEmpty(gameDirectory) && Directory.Exists(gameDirectory))
            {
                string[] files = Directory.GetFiles(savegameDirectory);

                foreach (string file in files)
                {
                    currentSavegamePath = file;

                    if (IsValidSavegame(file))
                    {
                        byte levelIndex = GetHeaderLevelIndex(file);

                        if (levelNames.ContainsKey(levelIndex))
                        {
                            string levelName = levelNames[levelIndex];

                            List<UInt16> timestamp;

                            using (FileStream fs = new FileStream(currentSavegamePath, FileMode.Open, FileAccess.Read))
                            {
                                timestamp = GetHeaderTimestamp(fs);
                            }

                            string displayName = $"{levelName} - {timestamp[0]:D2}/{timestamp[1]:D2}/{timestamp[2]:D4} {timestamp[3]:D2}:{timestamp[4]:D2}:{timestamp[5]:D2}";

                            Savegame savegame = new Savegame
                            {
                                DisplayName = displayName,
                                FileName = file
                            };

                            savegames.Add(savegame);
                        }
                    }
                }

                cmbSavegame.DataSource = savegames;
                cmbSavegame.DisplayMember = "DisplayName";
                cmbSavegame.ValueMember = "FileName";
            }
        }

        private void RefreshSavegames()
        {
            if (string.IsNullOrEmpty(savegameDirectory) || !Directory.Exists(savegameDirectory))
            {
                return;
            }

            string previouslySelectedFile = cmbSavegame.SelectedValue as string;

            var newSavegames = new List<Savegame>();

            foreach (string file in Directory.GetFiles(savegameDirectory))
            {
                if (!IsValidSavegame(file))
                {
                    continue;
                }

                byte levelIndex = GetHeaderLevelIndex(file);

                if (!levelNames.ContainsKey(levelIndex))
                {
                    continue;
                }

                string levelName = levelNames[levelIndex];

                List<UInt16> timestamp;
                using (FileStream fs = new FileStream(file, FileMode.Open, FileAccess.Read))
                {
                    timestamp = GetHeaderTimestamp(fs);
                }

                string displayName =
                    $"{levelName} - {timestamp[0]:D2}/{timestamp[1]:D2}/{timestamp[2]:D4} " +
                    $"{timestamp[3]:D2}:{timestamp[4]:D2}:{timestamp[5]:D2}";

                newSavegames.Add(new Savegame
                {
                    DisplayName = displayName,
                    FileName = file
                });
            }

            savegames = newSavegames;

            cmbSavegame.DataSource = null;
            cmbSavegame.DataSource = savegames;
            cmbSavegame.DisplayMember = "DisplayName";
            cmbSavegame.ValueMember = "FileName";

            // Restore selection if file still exists
            if (!string.IsNullOrEmpty(previouslySelectedFile))
            {
                cmbSavegame.SelectedValue = previouslySelectedFile;
            }
        }

        private byte[] ParseCompressedBlock(Savegame savegame, int compressedBlockSize)
        {
            string filePath = savegame.FileName;
            List<byte> compressedBlockData = new List<byte>();

            using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            using (BinaryReader reader = new BinaryReader(fs))
            {
                int compressedBlockStart = HEADER_SIZE;

                reader.BaseStream.Seek(compressedBlockStart, SeekOrigin.Begin);

                // Read exactly compressedBlockSize bytes
                for (int i = 0; i < compressedBlockSize; i++)
                {
                    byte value = reader.ReadByte();
                    compressedBlockData.Add(value);
                }
            }

            return compressedBlockData.ToArray();
        }

        private byte[] ParseRemainingBlock(Savegame savegame, int logicalPrefixLen)
        {
            using (FileStream fs = new FileStream(currentSavegamePath, FileMode.Open, FileAccess.Read))
            {
                fs.Seek(HEADER_SIZE + logicalPrefixLen, SeekOrigin.Begin);

                int remainingSize = PAYLOAD_SIZE - logicalPrefixLen;

                byte[] tail = new byte[remainingSize];
                fs.Read(tail, 0, remainingSize);

                return tail;
            }
        }

        private byte[] Unpack(byte[] compressedData)
        {
            // The skip table
            byte[] offsetTable = new byte[8] { 0x00, 0x3C, 0x18, 0x54, 0x30, 0x0C, 0x48, 0x24 };

            // Constants
            int MAX_BUFFER_SIZE = COMPRESSED_BLOCK_MAX_SIZE;
            int LZW_BUFFER_SIZE = 0x1000;  // 4096 entries
            int[] LZW_BUFFER = new int[LZW_BUFFER_SIZE];
            byte[] output_buffer = new byte[MAX_BUFFER_SIZE];  // Decompressed output buffer
            int output_pos = 0;

            // Check for LZW header
            if (compressedData.Length >= 3 &&
                compressedData[0] == 0x1F &&
                compressedData[1] == 0x9D &&
                compressedData[2] == 0x8C)
            {
                // Setup
                int uVar8 = 3 & 3;    // starting offset after header
                int uVar9 = uVar8 * 8;
                uint uVar7 = 0x100;   // next dictionary index
                uint uVar10 = 0x1FF;  // dictionary mask for 9 bits
                int local_2c = 9;     // starting bit length
                int local_20 = uVar9; // remember offset at last CLEAR

                // Main loop
                while (uVar9 <= ((compressedData.Length - 3 + uVar8) * 8 - 9))
                {
                    // Increase code size if dictionary is about full
                    if (uVar10 < uVar7 && local_2c < 12)
                    {
                        local_2c += 1;
                        uVar10 = (uVar10 * 2) + 1;
                    }

                    // Record new dictionary code's output offset
                    if (uVar7 < 0x1000)
                    {
                        LZW_BUFFER[(int)(uVar7 - 256)] = output_pos;
                    }

                    // Extract bits from compressedBlockData
                    int shift_amt = uVar9 & 0x1F;
                    int byte_offset = (uVar9 >> 5) * 4 + (3 - uVar8);

                    if (byte_offset + 4 > compressedData.Length)
                    {
                        Debug.WriteLine($"[UNPACK] Byte offset 0x{byte_offset:X} is out of bounds!");
                        break;
                    }

                    uint uVar3 = BitConverter.ToUInt32(compressedData, byte_offset);

                    if (shift_amt != 0)
                    {
                        int sVar4 = shift_amt;
                        int next_offset = byte_offset + 4;

                        if (next_offset + 4 > compressedData.Length)
                        {
                            Debug.WriteLine($"[UNPACK] Next offset 0x{next_offset:X} is out of bounds!");
                            break;
                        }

                        uVar3 = (uVar3 >> sVar4) | (BitConverter.ToUInt32(compressedData, next_offset) << ((32 - sVar4) & 0x1F));
                    }

                    uVar3 &= uVar10;
                    uVar9 += local_2c;

                    // Handle LZW codes
                    if (uVar3 == 0x100)
                    {
                        // CLEAR code
                        local_2c = 9;
                        // Use the actual table skip:
                        int bitsSinceLastClear = uVar9 - local_20;
                        int index = (bitsSinceLastClear >> 2) & 7;     // (uVar11 - uVar13) >> 2 & 7
                        byte skip = offsetTable[index];
                        uVar9 += skip; // add the table-based offset

                        // Reset dictionary
                        uVar7 = 0x100;
                        uVar10 = 0x1FF;
                        local_20 = uVar9;
                    }
                    else if (uVar3 < 0x100)
                    {
                        // Direct literal byte
                        if (output_pos >= output_buffer.Length)
                        {
                            Debug.WriteLine("[UNPACK] Output position exceeds buffer size!");
                            break;
                        }

                        output_buffer[output_pos++] = (byte)uVar3;
                        uVar7++;
                    }
                    else
                    {
                        // Dictionary-based copy
                        int idx1 = (int)(uVar3 - 257);
                        int idx2 = (int)(uVar3 - 256);

                        if (idx1 >= LZW_BUFFER.Length || idx2 >= LZW_BUFFER.Length)
                        {
                            Debug.WriteLine("[UNPACK] LZW buffer index out of bounds!");
                            break;
                        }

                        int puVar1 = LZW_BUFFER[idx1];
                        int puVar2 = LZW_BUFFER[idx2];

                        if (puVar1 >= output_buffer.Length || puVar2 >= output_buffer.Length)
                        {
                            Debug.WriteLine($"[UNPACK] Invalid access to output buffer: puVar1={puVar1}, puVar2={puVar2}");
                            break;
                        }
                        if (output_pos >= output_buffer.Length)
                        {
                            Debug.WriteLine("[UNPACK] Output position exceeds buffer size!");
                            break;
                        }

                        // Copy the first byte
                        output_buffer[output_pos++] = output_buffer[puVar1++];

                        // Copy the rest
                        while (puVar1 <= puVar2)
                        {
                            if (output_pos >= output_buffer.Length)
                            {
                                Debug.WriteLine("[UNPACK] Output position exceeds buffer size!");
                                break;
                            }

                            output_buffer[output_pos++] = output_buffer[puVar1++];
                        }

                        uVar7++;
                    }
                }

                // Return decompressed data
                byte[] result = new byte[output_pos];
                Array.Copy(output_buffer, result, output_pos);
                return result;
            }
            else
            {
                string errorMessage = $"Invalid LZW header. Savegame is possibly corrupt.";
                throw new Exception(errorMessage);
            }
        }

        private byte[] Pack(byte[] rawData)
        {
            const int MAX_BITS = 12;
            const int INIT_BITS = 9;
            const uint MAX_CODE = (1U << MAX_BITS) - 1;
            const uint CLEAR_CODE = 0x100;
            const uint FIRST_CODE = 0x101;
            const int HASH_SIZE = 0x1400;

            // Write the 3-byte header.
            List<byte> destBuffer = new List<byte> { 0x1F, 0x9D, 0x8C };

            // Bit-packing state.
            ulong bitBuffer = 0;
            int bitCount = 0;
            // Start after the header: 3 bytes = 24 bits.
            int bitTotal = 24;

            // Dictionary.
            uint[] dictionary = new uint[HASH_SIZE];
            int codeWidth = INIT_BITS;
            uint maxCode = (1U << codeWidth) - 1;
            uint nextCode = FIRST_CODE;

            // Keep track of the bit offset at the beginning of the current dictionary block.
            int blockBase = bitTotal;

            if (rawData.Length == 0)
            {
                return destBuffer.ToArray();
            }

            uint currentCode = rawData[0];
            int inputPos = 1;

            // Clear code table
            byte[] clearTable = new byte[8] { 0x00, 0x3C, 0x18, 0x54, 0x30, 0x0C, 0x48, 0x24 };

            void WriteBits(uint code, int width)
            {
                bitBuffer |= ((ulong)code << bitCount);
                bitCount += width;
                bitTotal += width;
                while (bitCount >= 8)
                {
                    byte outByte = (byte)(bitBuffer & 0xFF);
                    destBuffer.Add(outByte);

                    bitBuffer >>= 8;
                    bitCount -= 8;
                }
            }

            void FlushBits()
            {
                if (bitCount > 0)
                {
                    byte finalByte = (byte)(bitBuffer & 0xFF);
                    destBuffer.Add(finalByte);

                    bitBuffer = 0;
                    bitCount = 0;
                }
            }

            // Main Compression Loop.
            while (inputPos < rawData.Length)
            {
                byte nextChar = rawData[inputPos++];
                uint combinedCode = (currentCode << 8) | nextChar;
                uint hashIndex = ((uint)nextChar << 4) ^ currentCode;
                hashIndex %= HASH_SIZE;

                bool found = false;
                uint step = (hashIndex == 0) ? 1u : (0x13FFu - hashIndex);

                while (dictionary[hashIndex] != 0)
                {
                    int entry = unchecked((int)dictionary[hashIndex]);
                    int adjusted = entry + ((entry >> 31) & 0xFFF);
                    if ((adjusted >> 12) == (int)combinedCode)
                    {
                        currentCode = (uint)(entry & 0xFFF);
                        found = true;
                        break;
                    }

                    // Apply probe arithmetic
                    int tempIndex = (int)hashIndex - (int)step;
                    if (tempIndex < 0)
                    {
                        tempIndex += 0x13FF; // wraparound
                    }

                    hashIndex = (uint)tempIndex;
                }

                if (!found)
                {
                    WriteBits(currentCode, codeWidth);

                    if (nextCode > maxCode && codeWidth < MAX_BITS)
                    {
                        codeWidth++;
                        maxCode = (1U << codeWidth) - 1;
                    }

                    if (nextCode <= MAX_CODE)
                    {
                        dictionary[hashIndex] = (combinedCode << 12) | nextCode;
                        nextCode++;
                    }
                    else
                    {
                        // Dictionary full: emit CLEAR code and then flush extra bits based on the clear table.
                        WriteBits(CLEAR_CODE, codeWidth);
                        // Compute how many bits have been output since the start of this dictionary block.
                        int bitsSince = bitTotal - blockBase;
                        int index = (bitsSince >> 2) & 7;
                        int extraBits = clearTable[index]; // extra bits to flush
                        WriteBits(0, extraBits);
                        // Reset dictionary and LZW state.
                        dictionary = new uint[HASH_SIZE];
                        codeWidth = INIT_BITS;
                        maxCode = (1U << codeWidth) - 1;
                        nextCode = FIRST_CODE;
                        // Reset blockBase to the current bit total.
                        blockBase = bitTotal;
                    }

                    currentCode = nextChar;
                }
            }

            // Final write.
            WriteBits(currentCode, codeWidth);
            FlushBits();

            return destBuffer.ToArray();
        }

        private uint ComputeHash(string input)
        {
            // Encode the string to bytes (ASCII encoding)
            byte[] data = Encoding.ASCII.GetBytes(input);

            // Find the first null byte (0x00) if present and truncate the string
            int nullIndex = Array.IndexOf(data, (byte)0);
            if (nullIndex != -1)
            {
                Array.Resize(ref data, nullIndex);
            }

            // Compute the length of the string up to the first null byte
            int length = data.Length;

            // Initialize uVar2 to the length of the string
            uint uVar2 = (uint)length;

            // Compute iVar5 as length shifted right by 6
            uint iVar5 = (uint)(length >> 6);

            // Initialize uVar3 to the length of the string
            uint uVar3 = (uint)length;

            // Initialize the index to traverse the byte array
            int index = 0;

            // Loop until uVar3 is greater than 0 and index is within bounds
            while (uVar3 > 0 && index < length)
            {
                // Retrieve the current byte value
                byte byte_val = data[index];

                // Perform arithmetic right shift
                int signedHash = unchecked((int)uVar2); // Interpret uVar2 as signed
                int shifted = signedHash >> 2; // Arithmetic right shift
                uint shiftedU = unchecked((uint)shifted); // Cast back to unsigned

                // Compute the addition
                uint addition = byte_val + (uVar2 * 32) + shiftedU;

                // Update uVar2 with XOR, ensuring it stays within 32 bits
                uVar2 = uVar2 ^ addition;
                uVar2 &= 0xFFFFFFFF; // Mask to 32 bits

                // Move to the next byte
                index += 1;

                // Decrement uVar3 by (iVar5 + 1)
                uVar3 -= (iVar5 + 1);
            }

            return uVar2;
        }

        private long FindCompressedStreamStart(byte[] fileData)
        {
            for (long i = 0; i < fileData.Length - 1; i++)
            {
                if (fileData[i] == 0x78) // First zlib byte
                {
                    byte secondByte = fileData[i + 1];

                    // Validate the second byte based on zlib flags
                    if ((secondByte & 0xF0) == 0x90 || (secondByte & 0xF0) == 0xD0)
                    {
                        return i;
                    }
                }
            }

            throw new Exception("Compressed stream not found in file.");
        }

        private int FindSecondMagicHeader(List<byte> data)
        {
            byte[] pattern = { 0x66, 0x66, 0x66, 0x40 };
            int foundCount = 0;

            for (int i = 0; i <= data.Count - pattern.Length; i++)
            {
                if (data[i] == pattern[0] &&
                    data[i + 1] == pattern[1] &&
                    data[i + 2] == pattern[2] &&
                    data[i + 3] == pattern[3])
                {
                    foundCount++;
                    if (foundCount == 2)
                    {
                        return i;  // second occurrence
                    }
                }
            }

            return -1;
        }

        // --------------------------------------------------------------------
        //  Reads the room count at (baseOffset+0x10),
        //  builds a list of room pointers, then for each room, reads the
        //  head pointer for (entityType). Returns all node offsets across rooms.
        // --------------------------------------------------------------------
        private List<int> GatherNodesForEntityType(int baseOffset, int entityType, int maxRooms = int.MaxValue)
        {
            if (baseOffset + 0x10 + 4 > decompressedGMX.Count)
            {
                return new List<int>();
            }

            // 1) room count
            int roomCount = ReadInt32FromGMX(baseOffset + 0x10);
            NUM_ROOMS = roomCount;

            // 2) read room pointers
            int pointerListOffset = baseOffset + 0x14;
            List<int> roomPointers = new List<int>();

            for (int i = 0; i < roomCount; i++)
            {
                int offset = pointerListOffset + i * 4;
                if (offset + 4 > decompressedGMX.Count)
                {
                    break;
                }

                int roomPtr = ReadInt32FromGMX(offset);
                roomPointers.Add(roomPtr);
            }

            // 3) gather all node offsets
            List<int> allNodeOffsets = new List<int>();
            int roomsToProcess = Math.Min(roomCount, maxRooms);

            rooms.Clear();

            for (int i = 0; i < roomsToProcess; i++)
            {
                int roomBase = baseOffset + roomPointers[i];

                EntityMock roomEntity = new EntityMock(roomBase);

                // Read the 0x15C value for each room
                int roomMetaOffset = roomBase + 0x15C;
                if (roomMetaOffset + 4 <= decompressedGMX.Count)
                {
                    int roomMeta = ReadInt32FromGMX(roomMetaOffset);
                    roomEntity.AddSubstructure(0x15C, roomMeta);
                    //Debug.WriteLine($"Room {i}: Metadata at 0x15C = 0x{roomMeta:X}");

                    // Read additional 32-bit value if 0x15C is not 0
                    if (roomMeta != 0)
                    {
                        int extraDataOffset = roomMeta;
                        if (extraDataOffset + 4 <= decompressedGMX.Count)
                        {
                            int extraData = ReadInt32FromGMX(extraDataOffset);
                            roomEntity.AddSubstructure(0x160, extraData);
                            //Debug.WriteLine($"Room {i}: Extra Data = 0x{extraData:X}");
                        }
                    }
                }

                rooms.Add(roomEntity);

                int headPointerOffset = roomBase + 0xB8 + entityType * 8;
                if (headPointerOffset + 4 > decompressedGMX.Count)
                {
                    continue;
                }

                int headPointer = ReadInt32FromGMX(headPointerOffset);
                if (headPointer != 0)
                {
                    var nodeOffsets = ExtractLinkedListNodeOffsets(headPointer, roomBase);
                    allNodeOffsets.AddRange(nodeOffsets);
                }
            }

            return allNodeOffsets;
        }

        // --------------------------------------------------------------------
        //  Singly-linked list: node+0..3 => data, node+4..7 => next pointer
        //  (relative to roomBase)
        //  Gather the absolute file offset for each node
        // --------------------------------------------------------------------
        private List<int> ExtractLinkedListNodeOffsets(int firstRelativePtr, int roomBase)
        {
            List<int> nodeOffsets = new List<int>();
            int current = firstRelativePtr;

            while (current != 0)
            {
                int absoluteOffset = roomBase + current;
                if (absoluteOffset + 8 > decompressedGMX.Count)
                {
                    break;
                }

                nodeOffsets.Add(absoluteOffset);

                // next pointer = *(absoluteOffset+4)
                int nextPtr = ReadInt32FromGMX(absoluteOffset + 4);
                current = nextPtr;
            }

            return nodeOffsets;
        }

        // --------------------------------------------------------------------
        //  Replicates mapBuildNodeHashTable's logic of dedup by node+100
        //  then sorts the final array by that hash
        //  IMPORTANT: read the hash as unsigned
        //  Returns List of (hashValue, nodeOffset)
        // --------------------------------------------------------------------
        private List<KeyValuePair<uint, int>> BuildEntityArray(List<int> nodeOffsets)
        {
            Dictionary<uint, int> dedup = new Dictionary<uint, int>();

            foreach (int nodeOff in nodeOffsets)
            {
                if (nodeOff + 104 > decompressedGMX.Count)
                {
                    continue;
                }

                // Read the 4-byte hash at nodeOff+100 as unsigned
                uint hashVal = ReadUInt32FromGMX(nodeOff + 100);

                if (!dedup.ContainsKey(hashVal))
                {
                    dedup[hashVal] = nodeOff;
                }
            }

            // sort by the unsigned hashVal
            return dedup.OrderBy(kvp => kvp.Key).ToList();
        }

        private int ReadInt32FromGMX(int offset)
        {
            if (offset < 0 || offset + 4 > decompressedGMX.Count)
            {
                return 0;
            }

            return BitConverter.ToInt32(decompressedGMX.GetRange(offset, 4).ToArray(), 0);
        }

        private uint ReadUInt32FromGMX(int offset)
        {
            if (offset < 0 || offset + 4 > decompressedGMX.Count)
            {
                return 0;
            }

            return BitConverter.ToUInt32(decompressedGMX.GetRange(offset, 4).ToArray(), 0);
        }

        private void MapLoadGMX(string mapName)
        {
            string gmxPath = $"{gameDirectory}\\data\\Maps\\{mapName}.GMX.CLZ";

            decompressedGMX.Clear();

            if (string.IsNullOrEmpty(gameDirectory) || !Directory.Exists(gameDirectory) || !File.Exists(gmxPath))
            {
                string errorMessage = $"Could not find GMX file.";
                throw new Exception(errorMessage);
            }

            Debug.WriteLine("GMX File Found!");

            try
            {
                byte[] fileData = File.ReadAllBytes(gmxPath);

                // Try to locate the compressed stream in the file
                using (MemoryStream memoryStream = new MemoryStream(fileData))
                {
                    memoryStream.Position = FindCompressedStreamStart(fileData);
                    using (InflaterInputStream inflater = new InflaterInputStream(memoryStream))
                    using (MemoryStream decompressedStream = new MemoryStream())
                    {
                        inflater.CopyTo(decompressedStream);
                        decompressedGMX = decompressedStream.ToArray().ToList();
                    }
                }

                Debug.WriteLine("GMX File Decompressed Successfully!");

                int baseOffset = FindSecondMagicHeader(decompressedGMX);
                if (baseOffset < 0)
                {
                    Debug.WriteLine("Could not find second occurrence of magic header.");
                    return;
                }

                List<int> nodeOffsets = GatherNodesForEntityType(baseOffset, 2);

                List<KeyValuePair<uint, int>> finalArray = BuildEntityArray(nodeOffsets);

                Debug.WriteLine($"Map: {mapName}.GMX.CLZ");

                actors.Clear();

                foreach (var kvp in finalArray)
                {
                    int nodeOffset = kvp.Value;
                    EntityMock actor = new EntityMock(nodeOffset);

                    // Save ID (Offset 0x170)
                    int idOffset = nodeOffset + 0x170;
                    if (idOffset + 4 <= decompressedGMX.Count)
                    {
                        int actorId = ReadInt32FromGMX(idOffset);
                        actor.ID = actorId;
                    }

                    // Handle other substructures
                    int[] substructureOffsets = { 0x028, 0x17C, 0x184, 0x18C };
                    foreach (int offset in substructureOffsets)
                    {
                        int substructureOffset = nodeOffset + offset;
                        if (substructureOffset + 4 <= decompressedGMX.Count)
                        {
                            int value = ReadInt32FromGMX(substructureOffset);
                            actor.AddSubstructure(offset, value);
                        }
                    }

                    actor.EntityType = 2;

                    actors.Add(actor);
                }

                // Display each Actor's properties
                Debug.WriteLine("\n=== Actor Properties ===\n");
                for (int i = 0; i < actors.Count; i++)
                {
                    EntityMock actor = actors[i];
                    Debug.WriteLine($"Actor #{i + 1} at Base Offset: 0x{actor.BaseOffset:X8}, ID: 0x{actor.ID:X8}");

                    foreach (var substructure in actor.Substructures)
                    {
                        Debug.WriteLine($"Offset 0x{substructure.Key:X}: Value = 0x{substructure.Value:X8}");
                    }

                    Debug.WriteLine("");
                }

                NUM_ACTORS = actors.Count;
                Debug.WriteLine($"Total Actors Parsed: {NUM_ACTORS}");
                Debug.WriteLine("");

                nodeOffsets.Clear();
                nodeOffsets = GatherNodesForEntityType(baseOffset, 0);

                finalArray.Clear();
                finalArray = BuildEntityArray(nodeOffsets);

                objects.Clear();

                foreach (var kvp in finalArray)
                {
                    int nodeOffset = kvp.Value;
                    EntityMock obj = new EntityMock(nodeOffset);

                    // Handle other substructures
                    int[] substructureOffsets = { 0x028, 0x17C, 0x184, 0x18C };
                    foreach (int offset in substructureOffsets)
                    {
                        int substructureOffset = nodeOffset + offset;
                        if (substructureOffset + 4 <= decompressedGMX.Count)
                        {
                            int value = ReadInt32FromGMX(substructureOffset);
                            obj.AddSubstructure(offset, value);
                        }
                    }

                    // Special handling for 0x172 (must be ushort)
                    int substructureOffset_172 = nodeOffset + 0x172;
                    if (substructureOffset_172 + 2 <= decompressedGMX.Count)
                    {
                        ushort value_172 = BitConverter.ToUInt16(decompressedGMX.GetRange(substructureOffset_172, 2).ToArray(), 0);
                        obj.AddSubstructure(0x172, value_172);
                    }

                    objects.Add(obj);
                }

                ParseGMXObjects();

                for (int i = 0; i < objects.Count; i++)
                {
                    if (!objects[i].Substructures.TryGetValue(0x172, out int objectTypeId))
                    {
                        Debug.WriteLine($"[ERROR] Object ID {objects[i]} has no 0x172 substructure. Skipping...");
                    }

                    objects[i].EntityType = 0;
                }

                // Display each Actor's properties
                Debug.WriteLine("\n=== Object Properties ===\n");
                for (int i = 0; i < objects.Count; i++)
                {
                    EntityMock obj = objects[i];
                }

                NUM_OBJECTS = objects.Count;
                Debug.WriteLine($"NUM_OBJECTS: {NUM_OBJECTS}");

                ParseGMXObjects();

                nodeOffsets.Clear();
                nodeOffsets = GatherNodesForEntityType(baseOffset, 3);

                finalArray.Clear();
                finalArray = BuildEntityArray(nodeOffsets);

                NUM_TRIGGERS = finalArray.Count;
                Debug.WriteLine($"NUM_TRIGGERS = {NUM_TRIGGERS}");

                nodeOffsets.Clear();
                nodeOffsets = GatherNodesForEntityType(baseOffset, 8);

                finalArray.Clear();
                finalArray = BuildEntityArray(nodeOffsets);

                NUM_EMITTERS = finalArray.Count;
                Debug.WriteLine($"NUM_EMITTERS = {NUM_EMITTERS}");

                nodeOffsets.Clear();
                nodeOffsets = GatherNodesForEntityType(baseOffset, 11);

                finalArray.Clear();
                finalArray = BuildEntityArray(nodeOffsets);

                NUM_AUDIO_LOCATORS = finalArray.Count;
                Debug.WriteLine($"NUM_AUDIO_LOCATORS = {NUM_AUDIO_LOCATORS}");
                Debug.WriteLine("");
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error decompressing GMX file: " + ex.Message + Environment.NewLine);
            }
        }

        private void ParseGMXObjects()
        {
            // Clear any previously stored objects
            gmxObjects.Clear();

            // Compute the hash for the map name + ".EVX"
            string evxFileName = $"{mapName}.EVX";
            uint hashValue = ComputeHash(evxFileName);
            //Debug.WriteLine($"[DEBUG] Computed Hash for {evxFileName}: 0x{hashValue:X8}");

            // Convert the hash value to an LE byte array
            byte[] hashBytes = BitConverter.GetBytes(hashValue);
            //Debug.WriteLine($"[DEBUG] Hash Bytes (Little Endian): {BitConverter.ToString(hashBytes)}");

            // Search for the hash in the decompressed GMX data
            int hashOffset = FindByteSequence(decompressedGMX, hashBytes);
            if (hashOffset == -1)
            {
                Debug.WriteLine($"[DEBUG] Hash 0x{hashValue:X8} not found in GMX file.");
                return;
            }
            //Debug.WriteLine($"[DEBUG] Hash 0x{hashValue:X8} found at GMX offset: 0x{hashOffset:X}");

            // Step 4: Read the next DWORD after the hash
            int nextDwordOffset = hashOffset + 4;
            if (nextDwordOffset + 4 > decompressedGMX.Count)
            {
                Debug.WriteLine($"[DEBUG] Next DWORD offset 0x{nextDwordOffset:X} is out of bounds.");
                return;
            }
            uint nextDword = BitConverter.ToUInt32(decompressedGMX.GetRange(nextDwordOffset, 4).ToArray(), 0);
            //Debug.WriteLine($"[DEBUG] Next DWORD value: 0x{nextDword:X8}");

            // Step 5: Add 0x800 to the DWORD
            // This becomes the base offset
            uint baseOffset = nextDword + 0x800;
            //Debug.WriteLine($"[DEBUG] EVX base offset = 0x{baseOffset:X}");

            // --- Header Parsing ---
            // At the base offset, the header holds:
            // DWORD at baseOffset      --> Pointer Table Offset (ptOffset)
            // DWORD at baseOffset + 4  --> Second Section Offset (points to records header)
            // DWORD at baseOffset + 8  --> Third value (for verification/debugging)
            uint ptOffset = BitConverter.ToUInt32(decompressedGMX.GetRange((int)baseOffset, 4).ToArray(), 0);
            uint secondSectionOffset = BitConverter.ToUInt32(decompressedGMX.GetRange((int)baseOffset + 4, 4).ToArray(), 0);
            uint thirdValue = BitConverter.ToUInt32(decompressedGMX.GetRange((int)baseOffset + 8, 4).ToArray(), 0);

            //Debug.WriteLine($"[DEBUG] Pointer Table Offset (from base): 0x{ptOffset:X}");
            //Debug.WriteLine($"[DEBUG] Second Section Offset (from base): 0x{secondSectionOffset:X}");
            //Debug.WriteLine($"[DEBUG] Third Value: 0x{thirdValue:X}");

            // --- Pointer Table Fixup ---
            // The pointer table header is located at baseOffset + ptOffset
            // Its first DWORD is the count of fixup entries
            uint ptHeaderOffset = baseOffset + ptOffset;
            uint ptCount = BitConverter.ToUInt32(decompressedGMX.GetRange((int)ptHeaderOffset, 4).ToArray(), 0);
            //Debug.WriteLine($"[DEBUG] Pointer Table Count: {ptCount}");

            // --- Records Parsing ---
            // The second section header is located at baseOffset + secondSectionOffset
            // Its first DWORD is the record count
            uint secondSectionHeaderOffset = baseOffset + secondSectionOffset;
            uint recordCount = BitConverter.ToUInt32(decompressedGMX.GetRange((int)secondSectionHeaderOffset, 4).ToArray(), 0);
            uint secondSectionFlag = BitConverter.ToUInt32(decompressedGMX.GetRange((int)secondSectionHeaderOffset + 4, 4).ToArray(), 0);

            //Debug.WriteLine($"[DEBUG] Second Section Record Count: {recordCount}");
            //Debug.WriteLine($"[DEBUG] Second Section Flag: 0x{secondSectionFlag:X}");

            // The records begin 0x10 bytes after the second section header
            uint recordsOffset = baseOffset + secondSectionOffset + 0x10;
            int recordSize = 0x60; // Each record is 0x60 bytes

            // Loop through each record.
            for (int i = 0; i < recordCount; i++)
            {
                int recOffset = (int)recordsOffset + i * recordSize;
                if (recOffset + recordSize > decompressedGMX.Count)
                {
                    Debug.WriteLine($"[DEBUG] Record {i} is out of bounds.");
                    break;
                }

                // Read the entire record
                List<byte> recordBytes = decompressedGMX.GetRange(recOffset, recordSize);

                // The record string is located at offset 0x40 within the record
                int strOffset = 0x40;
                int strLength = recordSize - strOffset; // Up to 0x20 bytes
                byte[] rawStrBytes = recordBytes.GetRange(strOffset, strLength).ToArray();

                // Look for a null terminator
                int nullIndex = Array.IndexOf(rawStrBytes, (byte)0);
                string recordStr = (nullIndex != -1)
                    ? System.Text.Encoding.ASCII.GetString(rawStrBytes, 0, nullIndex)
                    : System.Text.Encoding.ASCII.GetString(rawStrBytes);

                // Create a new GmxObjectInfo and add it to the list
                GmxObjectInfo objInfo = new GmxObjectInfo()
                {
                    Index = i,
                    Name = recordStr
                };
                gmxObjects.Add(objInfo);
            }

            // Print the parsed records
            foreach (var obj in gmxObjects)
            {
                Debug.WriteLine($"Record {obj.Index}: {obj.Name}");
            }

            Debug.WriteLine("");
        }

        private void ParseSavegameData(Savegame savegame)
        {
            isLoading = true;

            try
            {
                currentSavegamePath = savegame.FileName;

                int compressedBlockSize = GetCompressedBlockSize();
                List<byte> compressedBlock = ParseCompressedBlock(savegame, compressedBlockSize).ToList();
                decompressedBlock = Unpack(compressedBlock.ToArray()).ToList();

                int logicalPrefixLen = decompressedBlock.Count;
                remainingBlock = ParseRemainingBlock(savegame, logicalPrefixLen).ToList();

                int logicalTotal = decompressedBlock.Count + remainingBlock.Count;

                // DEBUG LOGS
                Debug.WriteLine("");
                Debug.WriteLine("=== SAVEGAME METADATA ===");
                Debug.WriteLine($"Savegame: {savegame.DisplayName}");
                Debug.WriteLine($"Path: {currentSavegamePath}");
                Debug.WriteLine($"Compressed Block Size (from file): 0x{compressedBlockSize:X}");
                Debug.WriteLine($"Actual compressedBlock.Count: 0x{compressedBlock.Count:X}");
                Debug.WriteLine($"Unpack() returned size: 0x{decompressedBlock.Count:X}");
                Debug.WriteLine($"Remaining Block Size: 0x{remainingBlock.Count:X}");
                Debug.WriteLine($"Logical reconstructed size: 0x{logicalTotal:X}");
                Debug.WriteLine($"Expected payload size: 0x32000");
                Debug.WriteLine($"Matches payload? {logicalTotal == 0x32000}");
                Debug.WriteLine("=========================");
                Debug.WriteLine("");

                slblStatus.Text = "Loading...";

                RunWithLoadingUI(() =>
                {
                    DisplaySavegameData(decompressedBlock, remainingBlock);
                });

                ResetInventoryDisplay();

                slblStatus.Text = "Savegame successfully loaded";
                cmbSavegame.Focus();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                slblStatus.Text = $"Error loading savegame";
            }

            isLoading = false;
        }

        private void RunWithLoadingUI(Action action)
        {
            SetLoadingState(true);
            Application.DoEvents(); // Allow repaint

            try
            {
                action();
            }
            finally
            {
                SetLoadingState(false);
            }
        }

        private void SetControlsEnabledByType(Control parent, bool enabled, params Type[] types)
        {
            foreach (Control ctrl in parent.Controls)
            {
                if (types.Contains(ctrl.GetType()))
                {
                    ctrl.Enabled = enabled;
                }

                if (ctrl.HasChildren)
                {
                    SetControlsEnabledByType(ctrl, enabled, types);
                }
            }
        }

        private void SetLoadingState(bool isLoading)
        {
            bool enabled = !isLoading;

            SetControlsEnabledByType(grpItems, enabled, typeof(NumericUpDown));
            SetControlsEnabledByType(grpWeapons, enabled, typeof(NumericUpDown), typeof(CheckBox));
            SetControlsEnabledByType(grpHealth, enabled, typeof(TrackBar));

            cmbInventory.Enabled = enabled;
            cmbSavegame.Enabled = enabled;
            btnExit.Enabled = enabled;
            btnBrowse.Enabled = enabled;
            btnAbout.Enabled = enabled;

            this.Cursor = isLoading ? Cursors.WaitCursor : Cursors.Default;
        }

        private void FeLoad(BinaryReader reader)
        {
            //Debug.WriteLine($"FeLoad Start = 0x{sgBufferCursor.ToString("X")}");

            // Read stack0xfffffffc[0] (1 byte)
            sgBufferCursor += 1;

            // Read stack0xfffffffc[1] (1 byte)
            sgBufferCursor += 1;

            // Read stack0xfffffffc[2] (1 byte)
            sgBufferCursor += 1;

            // Read stack0xfffffffc[3] (1 byte)
            sgBufferCursor += 1;

            // Read DAT_00366f28 (4 bytes)
            sgBufferCursor += 4;

            // Read DAT_00366f24 (4 bytes)
            sgBufferCursor += 4;

            Debug.WriteLine($"FeLoad End = 0x{sgBufferCursor.ToString("X")}");
            return;
        }

        private void InvLoad(BinaryReader reader)
        {
            //Debug.WriteLine($"InvLoad Start = 0x{sgBufferCursor:X}");

            // Read gGameCash (4 bytes)
            reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
            Int32 gGameCash = reader.ReadInt32();
            nudCash.Value = gGameCash;
            sgBufferCursor += 4;

            // Read gConversationFlags (4 bytes)
            sgBufferCursor += 4;

            // gConversationState - 21 bytes (0x15 iterations of 1 byte each)
            for (int i = 0; i < 0x15; i++)
            {
                sgBufferCursor += 1;
            }

            // DAT_002dce28 - (increments by 0x38, reads 4 bytes each time until 0xC78)
            for (int i = 0; i < 0xC78; i += 0x38)
            {
                sgBufferCursor += 4;
            }

            // gSpecialCaseFlags - 4 bytes
            sgBufferCursor += 4;

            // gInvPlayerUpgradeLowerBody - 4 bytes
            sgBufferCursor += 4;

            // gInvPlayerUpgradeUpperBody - 4 bytes
            sgBufferCursor += 4;

            // invDiaryActive - 3 iterations of 4 bytes each
            for (int i = 0; i < 0xC; i += 4)
            {
                sgBufferCursor += 4;
            }

            // DAT_002dfe32 - (increments by 0x6C, reads 2 bytes each time until 0x654)
            for (int i = 0; i < 0x654; i += 0x6C)
            {
                sgBufferCursor += 2;
            }

            Debug.WriteLine($"InvLoad End = 0x{sgBufferCursor:X}");
            return;
        }

        private void MapLoadGlobals(BinaryReader reader)
        {
            Debug.WriteLine($"MapLoadGlobals Start: 0x{sgBufferCursor.ToString("X")}");

            // Read gmapPlayerEvent (0x80 bits -> 0x10 bytes)
            sgBufferCursor += 0x10;

            // Read DAT_00461ef0 (4 bytes)
            sgBufferCursor += 4;

            // Read DAT_00461ef4 (4 bytes)
            sgBufferCursor += 4;

            // Read DAT_00461ef8 (4 bytes)
            sgBufferCursor += 4;

            // Read DAT_00461efc (4 bytes)
            sgBufferCursor += 4;

            // Read stack0xfffffff4 (4 bytes)
            sgBufferCursor += 4;

            // Read DAT_00461f18 (4 bytes)
            sgBufferCursor += 4;

            // Read DAT_00461f1c (4 bytes)
            sgBufferCursor += 4;

            // Loop for gParis51FlagList array
            int uVar1 = 4;
            while (uVar1 < 0x44)
            {
                // Read first 4 bytes in iteration
                sgBufferCursor += 4;

                // Read next 4 bytes in iteration
                sgBufferCursor += 4;

                uVar1 += 8;
            }

            Debug.WriteLine($"MapLoadGlobals End: 0x{sgBufferCursor.ToString("X")}");
            return;
        }

        private int GetObjectAPBLoopCounter(int objectId)
        {
            // Ensure GMX objects were successfully parsed
            if (gmxObjects.Count == 0)
            {
                Debug.WriteLine("[ERROR] GMX object list is empty.");
                return 0;
            }

            // Validate the Object ID
            if (objectId < 0 || objectId >= objects.Count)
            {
                Debug.WriteLine($"[ERROR] Object ID {objectId} is out of bounds (Max: {objects.Count - 1}).");
                return 0;
            }

            var runtimeObject = objects[objectId];

            // Retrieve ID from 0x172 substructure
            if (!runtimeObject.Substructures.TryGetValue(0x172, out int objectTypeId))
            {
                Debug.WriteLine($"[ERROR] Object ID {objectId} has no 0x172 substructure. Skipping...");
                return 0;
            }

            // Compute GMX Object Index
            int gmxIndex = objectId;

            if (gmxIndex < 0 || gmxIndex >= gmxObjects.Count)
            {
                Debug.WriteLine($"[ERROR] Computed GMX index {gmxIndex} is out of bounds.");
                return 0;
            }

            var gmxObject = gmxObjects[gmxIndex];

            // Ignore NULL objects
            if (gmxObject.Name == "__NULL__")
            {
                Debug.WriteLine($"[INFO] Object ID {objectId} maps to NULL GMX Object (gmxIndex = {gmxIndex}). Returning 0...");
                return 0;
            }

            // Compute the hash for "{ObjectName}.CHR"
            string chrFileName = $"{gmxObject.Name}.CHR".ToUpper();
            uint hashValue = ComputeHash(chrFileName);

            // Convert hash value to little endian
            byte[] hashBytes = BitConverter.GetBytes(hashValue);

            // Search for the hash in the GMX file
            int hashOffset = FindByteSequence(decompressedGMX, hashBytes);
            if (hashOffset == -1)
            {
                Debug.WriteLine($"[ERROR] Hash 0x{hashValue:X8} not found in GMX file.");
                return 0;
            }

            // Read next DWORD (pointer offset)
            int nextDwordOffset = hashOffset + 4;
            if (nextDwordOffset + 4 > decompressedGMX.Count)
            {
                Debug.WriteLine($"[ERROR] Next DWORD offset 0x{nextDwordOffset:X} is out of bounds.");
                return 0;
            }

            uint nextDword = BitConverter.ToUInt32(decompressedGMX.GetRange(nextDwordOffset, 4).ToArray(), 0);

            // Compute APB Data Offset
            uint apbDataOffset = nextDword + 0x800;
            if (apbDataOffset + 0x8 + 4 > decompressedGMX.Count)  // Ensure 4 bytes can be read at apbDataOffset + 0x8
            {
                Debug.WriteLine($"[ERROR] APB Data Offset 0x{apbDataOffset:X8} is out of bounds.");
                return 0;
            }

            // Read loop counter
            uint loopCounterOffset = apbDataOffset + 0x8;
            int loopCounter = BitConverter.ToInt32(decompressedGMX.GetRange((int)loopCounterOffset, 4).ToArray(), 0);

            //Debug.WriteLine($"[INFO] APB Loop Counter for Object ID {objectId} (GMX Index {gmxIndex}, {gmxObject.Name}) = 0x{loopCounter:X}");

            return loopCounter;
        }

        private int GetActorAPBLoopCounter(int actorId)
        {
            // Query ActorDB using actorId
            if (!ActorDB.TryGetValue(actorId, out Actor actor) || string.IsNullOrEmpty(actor.Name))
            {
                Debug.WriteLine($"[DEBUG] Actor ID: 0x{actorId:X}, Name not found in ActorDB.");
                return 0;
            }

            // Compute the hash for the actor's name + ".CHR"
            string chrFileName = $"{actor.Name}.CHR";
            uint hashValue = ComputeHash(chrFileName);
            //Debug.WriteLine($"[DEBUG] Computed Hash for {chrFileName}: 0x{hashValue:X8}");

            // Convert the hash value to LE byte array
            byte[] hashBytes = BitConverter.GetBytes(hashValue);
            //Debug.WriteLine($"[DEBUG] Hash Bytes (Little Endian): {BitConverter.ToString(hashBytes)}");

            // Search for the hash in the decompressed GMX data
            int hashOffset = FindByteSequence(decompressedGMX, hashBytes);
            if (hashOffset == -1)
            {
                Debug.WriteLine($"[DEBUG] Hash 0x{hashValue:X8} not found in GMX file.");
                return 0;
            }

            //Debug.WriteLine($"[DEBUG] Hash 0x{hashValue:X8} found at GMX offset: 0x{hashOffset:X}");

            // Step 4: Read the next DWORD after the hash
            int nextDwordOffset = hashOffset + 4;
            if (nextDwordOffset + 4 > decompressedGMX.Count)
            {
                Debug.WriteLine($"[DEBUG] Next DWORD offset 0x{nextDwordOffset:X} is out of bounds.");
                return 0;
            }
            uint nextDword = BitConverter.ToUInt32(decompressedGMX.GetRange(nextDwordOffset, 4).ToArray(), 0);
            //Debug.WriteLine($"[DEBUG] Next DWORD value: 0x{nextDword:X8}");

            // Step 5: Add 0x800 to the DWORD value to get APB data offset
            uint apbDataOffset = nextDword + 0x800;
            //Debug.WriteLine($"[DEBUG] APB Data Offset: 0x{apbDataOffset:X8}");

            // Step 6: Read the DWORD at (APB data offset + 0x15) as the APB Loop Counter
            uint loopCounterOffset = apbDataOffset + 0x14;
            if (loopCounterOffset + 4 > decompressedGMX.Count)
            {
                Debug.WriteLine($"[DEBUG] APB Loop Counter offset 0x{loopCounterOffset:X} is out of bounds.");
                return 0;
            }

            int apbLoopCounter = BitConverter.ToInt32(decompressedGMX.GetRange((int)loopCounterOffset, 4).ToArray(), 0);
            //Debug.WriteLine($"[DEBUG] APB Loop Counter for {chrFileName}: 0x{apbLoopCounter:X}");
            return apbLoopCounter;
        }

        private int GetSecondAPBValue(int actorId)
        {
            // Step 1: Look up actor name in ActorDB
            if (!ActorDB.TryGetValue(actorId, out Actor actor) || string.IsNullOrEmpty(actor.Name))
            {
                Debug.WriteLine($"[DEBUG] Actor ID: 0x{actorId:X}, Name not found in ActorDB.");
                return 0;
            }

            // Step 2: Compute the GMX hash for actor's .CHR file
            string chrFileName = $"{actor.Name}.CHR";
            uint hashValue = ComputeHash(chrFileName);
            byte[] hashBytes = BitConverter.GetBytes(hashValue);

            // Step 3: Locate the hash in the decompressed GMX data
            int hashOffset = FindByteSequence(decompressedGMX, hashBytes);
            if (hashOffset == -1)
            {
                Debug.WriteLine($"[DEBUG] Hash 0x{hashValue:X8} ({chrFileName}) not found in GMX file.");
                return 0;
            }

            // Step 4: Read the DWORD immediately after the hash
            int nextDwordOffset = hashOffset + 4;
            if (nextDwordOffset + 4 > decompressedGMX.Count)
            {
                Debug.WriteLine($"[DEBUG] Next DWORD offset 0x{nextDwordOffset:X} is out of bounds.");
                return 0;
            }

            uint nextDword = BitConverter.ToUInt32(decompressedGMX.GetRange(nextDwordOffset, 4).ToArray(), 0);

            // Step 5: Compute the APB data offset
            uint apbDataOffset = nextDword + 0x800;
            //Debug.WriteLine($"[DEBUG] APB Data Offset: 0x{apbDataOffset:X8}");

            if (apbDataOffset >= decompressedGMX.Count)
            {
                Debug.WriteLine("[DEBUG] APB Data offset is out of bounds.");
                return 0;
            }

            // Get a byte array for the APB data block (starting at the computed offset)
            byte[] apbData = decompressedGMX.Skip((int)apbDataOffset).ToArray();

            int offset = 0;  // offset into apbData

            // --- Process First Loop ---

            if (apbData.Length < 0x10 + 4)
            {
                Debug.WriteLine("[DEBUG] APB data too short for first loop base value.");
                return 0;
            }

            uint baseVal = BitConverter.ToUInt32(apbData, 0x10);
            int loopCountOffset = (int)(baseVal + 0xC);

            if (loopCountOffset + 4 > apbData.Length)
            {
                Debug.WriteLine("[DEBUG] APB data too short for first loop count.");
                return 0;
            }

            uint firstLoopCount = BitConverter.ToUInt32(apbData, loopCountOffset);
            int recordAreaOffset = loopCountOffset + 4;
            offset = recordAreaOffset + (int)(firstLoopCount * 0x26);

            // --- Process Second Loop ---

            if (offset + 4 > apbData.Length)
            {
                Debug.WriteLine("[DEBUG] APB data too short for second loop.");
                return 0;
            }

            uint secondLoopCount = BitConverter.ToUInt32(apbData, offset);
            offset += 4 + (int)(secondLoopCount * 2);

            // --- Process Third Loop ---

            if (offset + 4 > apbData.Length)
            {
                Debug.WriteLine("[DEBUG] APB data too short for third loop.");
                return 0;
            }
            uint thirdLoopCount = BitConverter.ToUInt32(apbData, offset);
            offset += 4 + (int)(thirdLoopCount * 12);

            // --- Finally, read the second APB value ---
            if (offset + 4 > apbData.Length)
            {
                Debug.WriteLine($"[DEBUG] Second APB value offset 0x{offset:X} is out of bounds.");
                return 0;
            }
            int secondApbValue = BitConverter.ToInt32(apbData, offset);
            //Debug.WriteLine($"[DEBUG] Second APB Value for {chrFileName}: 0x{secondApbValue:X}");

            return secondApbValue;
        }

        private int FindByteSequence(List<byte> data, byte[] sequence)
        {
            if (sequence.Length == 0 || data.Count < sequence.Length)
            {
                return -1;
            }

            for (int i = 0; i <= data.Count - sequence.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < sequence.Length; j++)
                {
                    if (data[i + j] != sequence[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                {
                    return i;
                }

            }
            return -1;
        }

        private void MapActorLoad(BinaryReader reader, EntityMock actor)
        {
            if (actor.Substructures.TryGetValue(0x184, out int offset184Value) && (offset184Value & 0x400000) != 0)
            {
                Debug.WriteLine("Offset 0x184 has the 0x400000 bit set. Returning early.");
                return;
            }

            MapLoadBaseNode(reader);

            //Debug.WriteLine($"Offset after MapLoadBaseNode: 0x{sgBufferCursor.ToString("X")}");

            reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
            reader.ReadInt32();
            sgBufferCursor += 4;

            bool isPlayer = IsActorPlayable(actor);
            if (isPlayer)
            {
                PlayLoad(reader);
            }
            else
            {
                ActorLoad(reader, actor);
                Debug.WriteLine($"Offset after ActorLoad: 0x{sgBufferCursor:X}");
            }

            reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
            float health = reader.ReadSingle(); // Entity or player health
            sgBufferCursor += 4;

            if (isPlayer && health > 0 && health <= 100)
            {
                trbHealth.Value = (int)(health);
                lblHealth.Text = $"{health}%";

                PLAYER_HEALTH_OFFSET = sgBufferCursor - 4;

                Debug.WriteLine($"PLAYER_HEALTH_OFFSET = 0x{PLAYER_CASH_OFFSET:X}");
                Debug.WriteLine($"PLAYER HEALTH = {health}");
            }

            reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
            reader.ReadInt32();
            sgBufferCursor += 4;

            reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
            ushort local_30 = reader.ReadByte();
            sgBufferCursor += 1;

            //Debug.WriteLine($"local_30 = 0x{local_30.ToString("X")}, on offset: 0x{sgBufferCursor.ToString("X")}");

            if (local_30 != 0)
            {
                //Debug.WriteLine($"APB_Load to be called, current offset: 0x{sgBufferCursor.ToString("X")}");

                APB_Load(reader, actor);

                if ((offset184Value & 0x80) != 0)
                {
                    //Debug.WriteLine("Offset_184 val condition satisfied");
                    byte[] local_f0 = new byte[192]; // 192 bytes = 0x600 bits
                    reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                    reader.Read(local_f0, 0, local_f0.Length);
                    sgBufferCursor += local_f0.Length;
                }
            }

            //Debug.WriteLine($"MapActorLoad End (up to final condition) = 0x{sgBufferCursor:X}");
            return;
        }

        private void APB_LoadAnimationInfo(BinaryReader reader)
        {
            reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
            reader.ReadInt32();
            sgBufferCursor += 4;

            reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
            reader.ReadInt32();
            sgBufferCursor += 4;

            reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
            reader.ReadInt16();
            sgBufferCursor += 2;

            reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
            reader.ReadInt16();
            sgBufferCursor += 2;

            reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
            reader.ReadInt32();
            sgBufferCursor += 4;

            reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
            reader.ReadInt16();
            sgBufferCursor += 2;

            reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
            reader.ReadInt16();
            sgBufferCursor += 2;

            reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
            reader.ReadInt32();
            sgBufferCursor += 4;

            reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
            reader.ReadInt32();
            sgBufferCursor += 4;

            reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
            reader.ReadInt32();
            sgBufferCursor += 4;

            reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
            reader.ReadInt32();
            sgBufferCursor += 4;

            return;
        }

        private void APB_LoadAnimationControl(BinaryReader reader, int param_2)
        {
            reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
            reader.ReadInt32();
            sgBufferCursor += 4;

            APB_LoadAnimationInfo(reader);

            if (param_2 != 0)
            {
                APB_LoadAnimationInfo(reader);
            }

            reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
            reader.ReadBytes(12);
            sgBufferCursor += 12;

            return;
        }

        private void APB_Load(BinaryReader reader, EntityMock entity, int objApbLoopCounter = 0)
        {
            reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
            ushort local_28 = reader.ReadByte();
            sgBufferCursor += 1;

            //Debug.WriteLine($"local_28 = 0x{local_28.ToString("X")} on offset: 0x{sgBufferCursor.ToString("X")}");

            if ((local_28 & 1) != 0)
            {
                Int32 param_1;

                reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                param_1 = reader.ReadInt32();
                sgBufferCursor += 4;

                reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                reader.ReadInt32();
                sgBufferCursor += 4;

                reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                reader.ReadBytes(16); // Reads 0x80 bits
                sgBufferCursor += 16;

                int apbLoopCounter = 0;

                if (entity != null)
                {
                    apbLoopCounter = GetActorAPBLoopCounter(entity.ID);
                }
                else
                {
                    apbLoopCounter = objApbLoopCounter;
                }

                if (apbLoopCounter > 0)
                {
                    int index = 0;

                    do
                    {
                        reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                        reader.ReadInt32();
                        sgBufferCursor += 4;

                        if (entity != null)
                        {
                            reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                            reader.ReadInt32();
                            sgBufferCursor += 4;

                            reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                            reader.ReadInt32();
                            sgBufferCursor += 4;
                        }

                        index++;
                    } while (index < apbLoopCounter);

                    //Debug.WriteLine($"Offset AFTER APB_Load LOOP: 0x{sgBufferCursor.ToString("X")}");
                }

                APB_LoadAnimationControl(reader, (local_28 & 2));
                //Debug.WriteLine($"Offset AFTER first APB_LoadAnimationControl called: 0x{sgBufferCursor.ToString("X")}");

                if ((local_28 & 4) != 0)
                {
                    APB_LoadAnimationControl(reader, (local_28 & 4));
                    //Debug.WriteLine($"Offset AFTER second APB_LoadAnimationControl called: 0x{sgBufferCursor.ToString("X")}");
                }
                if ((local_28 & 8) != 0)
                {
                    APB_LoadAnimationControl(reader, (local_28 & 8));
                    //Debug.WriteLine($"Offset AFTER third APB_LoadAnimationControl called: 0x{sgBufferCursor.ToString("X")}");
                }

                reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                reader.ReadInt32();
                sgBufferCursor += 4;

                reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                reader.ReadInt32();
                sgBufferCursor += 4;

                reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                reader.ReadInt32();
                sgBufferCursor += 4;

                reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                reader.ReadInt32();
                sgBufferCursor += 4;

                reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                reader.ReadInt32();
                sgBufferCursor += 4;

                //Debug.WriteLine($"Offset BEFORE LARGE sgReadBits CALL IN APB_Load: 0x{sgBufferCursor.ToString("X")}");

                reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                reader.ReadBytes(144);
                sgBufferCursor += 144;

                //Debug.WriteLine($"Offset AFTER LARGE sgReadBits CALL IN APB_Load: 0x{sgBufferCursor.ToString("X")}");

                if ((param_1 & 0x80000) == 0)
                {
                    //Debug.WriteLine("param_1 & 0x80000 condition satisfied");
                    int secondApbValue = entity != null ? GetSecondAPBValue(entity.ID) : 0;

                    reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                    reader.ReadBytes((secondApbValue * 0x20) / 8);
                    sgBufferCursor += ((secondApbValue * 0x20) / 8);
                    //Debug.WriteLine($"Offset AFTER (param_1 & 0x80000) cond: 0x{sgBufferCursor.ToString("X")}");
                }
                else
                {
                    if (apbLoopCounter > 0) // TODO: Need to evaluate how to handle this case more gracefully...
                    {
                        //Debug.WriteLine("param_1 & 0x80000 condition NOT satisfied");
                        reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                        reader.ReadBytes((apbLoopCounter * 0x20) / 8);
                        sgBufferCursor += ((apbLoopCounter * 0x20) / 8);
                    }
                }

                //Debug.WriteLine($"Offset BEFORE SECOND LARGE sgReadBits CALL IN APB_Load: 0x{sgBufferCursor.ToString("X")}");

                reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                reader.ReadBytes(32);
                sgBufferCursor += 32;

                //Debug.WriteLine($"Offset AFTER SECOND LARGE sgReadBits CALL IN APB_Load: 0x{sgBufferCursor.ToString("X")}");
            }

            //Debug.WriteLine($"Offset AFTER APB_Load: 0x{sgBufferCursor.ToString("X")}");
            return;
        }

        private void ActorLoad(BinaryReader reader, EntityMock actor)
        {
            //Debug.WriteLine($"ActorLoad Start = 0x{sgBufferCursor:X}");

            if (true)
            {
                // Read 0x10 bits (2 bytes) for six different addresses
                reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                int offsetD60Value = reader.ReadInt16();
                sgBufferCursor += 2;

                reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                reader.ReadInt16();
                sgBufferCursor += 2;

                reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                reader.ReadInt16();
                sgBufferCursor += 2;

                reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                reader.ReadInt16();
                sgBufferCursor += 2;

                reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                reader.ReadInt16();
                sgBufferCursor += 2;

                reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                reader.ReadInt16();
                sgBufferCursor += 2;

                if (299 < offsetD60Value && offsetD60Value < 500)
                {
                    //Debug.WriteLine("***** CALLING PathLoad *****");
                    //Debug.WriteLine($"Offset before calling PathLoad: 0x{sgBufferCursor:X}");
                    PathLoad(reader);
                }

                //Debug.WriteLine($"****** ABOUT TO CALL BoneControlLoad 4x, Offset before calls: 0x{sgBufferCursor.ToString("X")} ******");

                // Call BoneControlLoad 4 times
                BoneControlLoad(reader);
                BoneControlLoad(reader);
                BoneControlLoad(reader);
                BoneControlLoad(reader);

                //Debug.WriteLine($"Offset after calling BoneControlLoad 4x: 0x{sgBufferCursor.ToString("X")}");
            }

            //Debug.WriteLine($"ActorLoad End = 0x{sgBufferCursor:X}");
            return;
        }

        private void PathLoad(BinaryReader reader)
        {
            //Debug.WriteLine($"PathLoad Start = 0x{sgBufferCursor:X}");

            // Read three 0x80-bit (16-byte) blocks
            for (int i = 0; i < 3; i++)
            {
                reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                reader.ReadBytes(16);
                sgBufferCursor += 16;
            }

            reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
            reader.ReadInt16();
            sgBufferCursor += 2;

            reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
            reader.ReadInt16();
            sgBufferCursor += 2;

            reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
            reader.ReadInt16();
            sgBufferCursor += 2;

            reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
            Int16 offset3E = reader.ReadInt16();
            sgBufferCursor += 2;

            // Conditional read if *(short *)(param_1 + 0x3e) == 1
            if (offset3E == 1)
            {
                reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                reader.ReadInt32();
                sgBufferCursor += 4;

                reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                reader.ReadInt32();
                sgBufferCursor += 4;
            }

            //Debug.WriteLine($"Offset before last 4 reads: 0x{sgBufferCursor:X}");

            reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
            reader.ReadInt16();
            sgBufferCursor += 2;

            reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
            reader.ReadInt16();
            sgBufferCursor += 2;

            reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
            Int16 Offset_44 = reader.ReadInt16();
            sgBufferCursor += 2;

            reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
            reader.ReadInt16();
            sgBufferCursor += 2;

            //Debug.WriteLine($"Offset before variable-length read: 0x{sgBufferCursor:X}");

            reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
            reader.ReadBytes((Offset_44 * 0x2) + 0x4);
            sgBufferCursor += ((Offset_44 * 0x2) + 0x4);

            //Debug.WriteLine($"Offset after variable-length read: 0x{sgBufferCursor:X}");

            Debug.WriteLine($"PathLoad End = 0x{sgBufferCursor:X}");
        }

        private void BoneControlLoad(BinaryReader reader)
        {
            //Debug.WriteLine($"BoneControlLoad Start = 0x{sgBufferCursor:X}");

            reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
            reader.ReadInt32();
            sgBufferCursor += 4;

            reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
            reader.ReadBytes(16);
            sgBufferCursor += 16;

            reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
            reader.ReadInt16();
            sgBufferCursor += 2;

            reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
            reader.ReadInt16();
            sgBufferCursor += 2;

            reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
            reader.ReadInt32();
            sgBufferCursor += 4;

            reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
            reader.ReadBytes(16);
            sgBufferCursor += 16;

            for (int i = 0; i < 8; i++)
            {
                reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                reader.ReadInt32();
                sgBufferCursor += 4;
            }

            //Debug.WriteLine($"BoneControlLoad End = 0x{sgBufferCursor:X}");
        }

        private void MapLoadBaseNode(BinaryReader reader)
        {
            // First sgReadBits call: 0x200 bits (0x40 bytes)
            sgBufferCursor += 0x40;

            // Second sgReadBits call: 0x20 bits (4 bytes)
            sgBufferCursor += 4;

            // Third sgReadBits call: 0x20 bits (4 bytes)
            sgBufferCursor += 4;

            // Fourth sgReadBits call: 0x400 bits (0x80 bytes)
            sgBufferCursor += 0x80;

            // Fifth sgReadBits call: 0x200 bits (0x40 bytes)
            sgBufferCursor += 0x40;

            //Debug.WriteLine($"MapLoadBaseNode End = 0x{sgBufferCursor:X}");
        }

        private void PlayLoad(BinaryReader reader)
        {
            Debug.WriteLine($"PlayLoad Start = 0x{sgBufferCursor:X}");

            // Read 10 Int32s
            for (int i = 0; i < 10; i++)
            {
                reader.ReadInt32();
                sgBufferCursor += 4;
            }

            // Read 1 byte
            byte local_1c = reader.ReadByte();
            sgBufferCursor += 1;

            // Read 1 byte
            byte local_18 = reader.ReadByte();
            sgBufferCursor += 1;

            // Read 3 Int32s
            for (int i = 0; i < 3; i++)
            {
                reader.ReadInt32();
                sgBufferCursor += 4;
            }

            // Read (0x80 bits -> 0x10 bytes)
            reader.ReadBytes(0x80);
            sgBufferCursor += 0x10;

            // Read 3 more Int32s
            for (int i = 0; i < 3; i++)
            {
                reader.ReadInt32();
                sgBufferCursor += 4;
            }

            // Read another byte
            reader.ReadByte();
            sgBufferCursor += 1;

            // Read 2 Int32s
            for (int i = 0; i < 2; i++)
            {
                reader.ReadInt32();
                sgBufferCursor += 4;
            }

            // Read 3 bytes
            for (int i = 0; i < 3; i++)
            {
                reader.ReadByte();
                sgBufferCursor += 1;
            }

            // Read 4 Int32s
            for (int i = 0; i < 4; i++)
            {
                reader.ReadInt32();
                sgBufferCursor += 4;
            }

            // Read 2 Int32s
            for (int i = 0; i < 2; i++)
            {
                reader.ReadInt32();
                sgBufferCursor += 4;
            }

            // Read 1 byte
            reader.ReadByte();
            sgBufferCursor += 1;

            // Read 2 more Int32s
            for (int i = 0; i < 2; i++)
            {
                reader.ReadInt32();
                sgBufferCursor += 4;
            }

            // Read (0x80 bits -> 0x10 bytes)
            reader.ReadBytes(0x80);
            sgBufferCursor += 0x10;

            // Read 5 Int32s
            for (int i = 0; i < 5; i++)
            {
                reader.ReadInt32();
                sgBufferCursor += 4;
            }

            // Conditional reads
            if ((local_1c & 0x80) != 0)
            {
                Debug.WriteLine("PlayLoad -- local_1c & 0x80: CONDITION SATISFIED");

                // Read 1 byte
                reader.ReadByte();
                sgBufferCursor += 1;

                // Read 1 byte
                reader.ReadByte();
                sgBufferCursor += 1;

                // Read (0x80 bits -> 0x10 bytes)
                reader.ReadBytes(0x80);
                sgBufferCursor += 0x10;

                // Read 11 Int32s
                for (int i = 0; i < 11; i++)
                {
                    reader.ReadInt32();
                    sgBufferCursor += 4;
                }

                // Read (0x80 bits -> 0x10 bytes)
                reader.ReadBytes(0x80);
                sgBufferCursor += 0x10;

                // Read 5 Int32s
                for (int i = 0; i < 5; i++)
                {
                    reader.ReadInt32();
                    sgBufferCursor += 4;
                }

                // Read (0x80 bits -> 0x10 bytes)
                reader.ReadBytes(0x80);
                sgBufferCursor += 0x10;

                // Read (0x80 bits -> 0x10 bytes)
                reader.ReadBytes(0x80);
                sgBufferCursor += 0x10;

                Debug.WriteLine($"Offset after conditional read in PlayLoad: 0x{sgBufferCursor.ToString("X")}");
            }

            // Read 1 byte
            reader.ReadByte();
            sgBufferCursor += 1;

            // Read (0x80 bits -> 0x10 bytes)
            reader.ReadBytes(0x80);
            sgBufferCursor += 0x10;

            // Read (0x80 bits -> 0x10 bytes)
            reader.ReadBytes(0x80);
            sgBufferCursor += 0x10;

            // Read 1 byte
            reader.ReadByte();
            sgBufferCursor += 1;

            // Read 1 byte
            reader.ReadByte();
            sgBufferCursor += 1;

            // Read 1 Int32
            reader.ReadInt32();
            sgBufferCursor += 4;

            // Read 1 byte
            reader.ReadByte();
            sgBufferCursor += 1;

            // Read (0x80 bits -> 0x10 bytes) 6 times
            for (int i = 0; i < 6; i++)
            {
                reader.ReadBytes(0x80);
                sgBufferCursor += 0x10;
            }

            // Read 1 byte
            reader.ReadByte();
            sgBufferCursor += 1;

            // Read 2 Int32s
            for (int i = 0; i < 2; i++)
            {
                reader.ReadInt32();
                sgBufferCursor += 4;
            }

            // Read another Int32
            reader.ReadInt32();
            sgBufferCursor += 4;

            // Read (another) another Int32
            reader.ReadInt32();
            sgBufferCursor += 4;

            // Read yet (another another) another Int32
            reader.ReadInt32();
            sgBufferCursor += 4;


            // Read (0x80 bits -> 0x10 bytes) 4 times
            for (int i = 0; i < 4; i++)
            {
                reader.ReadBytes(0x80);
                sgBufferCursor += 0x10;
            }

            // Read 1 byte
            reader.ReadByte();
            sgBufferCursor += 1;

            // Read (0x80 bits -> 0x10 bytes)
            reader.ReadBytes(0x80);
            sgBufferCursor += 0x10;

            // Read 2 Int32s
            for (int i = 0; i < 2; i++)
            {
                reader.ReadInt32();
                sgBufferCursor += 4;
            }

            // Read (0x80 bits -> 0x10 bytes) 2 times
            for (int i = 0; i < 2; i++)
            {
                reader.ReadBytes(0x80);
                sgBufferCursor += 0x10;
            }

            // Read 1 byte
            reader.ReadByte();
            sgBufferCursor += 1;

            // Read (0x80 bits -> 0x10 bytes) 3 times
            for (int i = 0; i < 3; i++)
            {
                reader.ReadBytes(0x80);
                sgBufferCursor += 0x10;
            }

            // Read Int32
            reader.ReadInt32();
            sgBufferCursor += 4;

            // Read 1 byte
            reader.ReadByte();
            sgBufferCursor += 1;

            // Read (0x80 bits -> 0x10 bytes) 3 times
            for (int i = 0; i < 3; i++)
            {
                reader.ReadBytes(0x80);
                sgBufferCursor += 0x10;
            }

            // Read 1 byte
            reader.ReadByte();
            sgBufferCursor += 1;

            // Read another byte
            reader.ReadByte();
            sgBufferCursor += 1;

            Debug.WriteLine($"Offset before BoneControlLoad: 0x{sgBufferCursor:X}");

            // Call BoneControlLoad 5x
            for (int i = 0; i < 5; i++)
            {
                BoneControlLoad(reader);
            }

            Debug.WriteLine($"PlayLoad End = 0x{sgBufferCursor:X}");
        }

        private void MapTrigLoad(BinaryReader reader)
        {
            MapLoadBaseNode(reader);

            sgBufferCursor += 4;

            return;
        }

        private void MapEmitterLoad(BinaryReader reader)
        {
            MapLoadBaseNode(reader);

            return;
        }

        private void MapObjLoad(BinaryReader reader, EntityMock obj)
        {
            MapLoadBaseNode(reader);

            reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
            Int32 Offset_0x168_Value = reader.ReadInt32();
            sgBufferCursor += 4;

            reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
            reader.ReadInt16();
            sgBufferCursor += 2;

            reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
            reader.ReadInt32();
            sgBufferCursor += 4;

            if ((Offset_0x168_Value & 0x40) != 0)
            {
                //Debug.WriteLine("((Offset_0x168_Value & 0x40) != 0) condition satisfied");
                reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                reader.ReadInt16();
                sgBufferCursor += 2;
            }

            reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
            byte stack0xfffffff4 = reader.ReadByte();
            sgBufferCursor += 1;

            //Debug.WriteLine($"stack0xfffffff4 value = 0x{stack0xfffffff4.ToString("X")}");

            if (stack0xfffffff4 != 0)
            {
                //Debug.WriteLine($"******** Calling APB_Load for Object, Current Offset: 0x{sgBufferCursor.ToString("X")}");

                int apbLoopCounter = 0;

                if (obj.Substructures.TryGetValue(0x172, out int objectTypeId))
                {
                    apbLoopCounter = GetObjectAPBLoopCounter(objectTypeId);
                    //Debug.WriteLine($"APB Loop Counter for object: 0x{apbLoopCounter.ToString("X")}");
                }
                else
                {
                    Debug.WriteLine("COULD NOT GET 0x172 VALUE");
                }

                APB_Load(reader, null, apbLoopCounter);
            }

            return;
        }

        private void MapLoad(BinaryReader reader)
        {
            Debug.WriteLine($"Map Block Start = 0x{sgBufferCursor:X}");

            // Load map globals first
            MapLoadGlobals(reader);

            // Load Actors
            for (int i = 0; i < NUM_ACTORS; i++)
            {
                //Debug.WriteLine($"*** Iteration {i + 1} of Actor loop ***");
                MapActorLoad(reader, actors[i]);
                Debug.WriteLine($"Offset after loading Actor #{i + 1}: 0x{sgBufferCursor.ToString("X")}");
            }

            Debug.WriteLine("");
            Debug.WriteLine($"Map Offset AFTER loading Actors: 0x{sgBufferCursor:X}");

            //Debug.WriteLine($"{NUM_OBJECTS} objects for current map.");
            for (int i = 0; i < NUM_OBJECTS; i++)
            {
                MapObjLoad(reader, objects[i]);
                //Debug.WriteLine($"Offset after loading Object #{i + 1}: 0x{sgBufferCursor.ToString("X")}");
            }

            Debug.WriteLine($"Map Offset AFTER loading Objects: 0x{sgBufferCursor.ToString("X")}");


            // Load Triggers
            for (int i = 0; i < NUM_TRIGGERS; i++)
            {
                MapTrigLoad(reader);
                //Debug.WriteLine($"Offset: 0x{sgBufferCursor.ToString("X")} at loop {i + 1} of mapTrigLoad loop");
            }

            Debug.WriteLine($"Map Offset AFTER loading Triggers: 0x{sgBufferCursor.ToString("X")}");


            // Load Emitters
            for (int i = 0; i < NUM_EMITTERS; i++)
            {
                MapEmitterLoad(reader);
                //Debug.WriteLine($"Offset: 0x{sgBufferCursor.ToString("X")} at loop {i + 1} of mapEmitterLoad loop");
            }

            Debug.WriteLine($"Map Offset AFTER loading Emitters: 0x{sgBufferCursor.ToString("X")}");

            // Load Water
            reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
            Int16 local_14 = reader.ReadInt16();
            sgBufferCursor += 2;

            //Debug.WriteLine($"local_14 = 0x{local_14.ToString("X")}");

            if (local_14 != 0)
            {
                int index = 0;
                do
                {
                    reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                    Int32 local_10 = reader.ReadInt32();
                    sgBufferCursor += 4;

                    MapLoadBaseNode(reader);

                    reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                    reader.ReadInt16();
                    sgBufferCursor += 2;

                    index = index + 1;
                } while (index < local_14);
            }

            Debug.WriteLine($"Map Offset AFTER loading Water: 0x{sgBufferCursor.ToString("X")}");


            // Load Audio Locators
            for (int i = 0; i < NUM_AUDIO_LOCATORS; i++)
            {
                MapLoadBaseNode(reader);
            }

            Debug.WriteLine($"Map Offset AFTER loading Audio Locators: 0x{sgBufferCursor.ToString("X")}");

            // Flip rooms
            for (int i = 0; i < NUM_ROOMS; i++)
            {
                if (rooms[i].Substructures.TryGetValue(0x15C, out int roomMeta))
                {
                    //Debug.WriteLine($"Room at 0x{rooms[i].BaseOffset:X} has Metadata 0x15C = 0x{roomMeta:X}");

                    if (roomMeta != 0)
                    {
                        // Do something with this value
                        int extraData = ReadInt32FromGMX(roomMeta);
                        //Debug.WriteLine($"Extra Data for Room at 0x{rooms[i].BaseOffset:X}: 0x{extraData:X}");

                        reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                        reader.ReadInt32();
                        sgBufferCursor += 4;
                    }
                }
            }

            Debug.WriteLine($"Map Block End = 0x{sgBufferCursor:X}");
            return;
        }

        private void DisplayInventoryItem(InventoryItem inventoryItem)
        {
            // -------------- HEALTH ITEMS -------------- //
            if (inventoryItem.ClassId == Inventory.CHOCOLATE_BAR)
            {
                nudChocolateBar.Value = inventoryItem.Quantity;
            }
            else if (inventoryItem.ClassId == Inventory.HEALTH_BANDAGES)
            {
                nudHealthBandages.Value = inventoryItem.Quantity;
            }
            else if (inventoryItem.ClassId == Inventory.HEALTH_PILLS)
            {
                nudHealthPills.Value = inventoryItem.Quantity;
            }
            else if (inventoryItem.ClassId == Inventory.LARGE_HEALTH_PACK)
            {
                nudLargeHealthPack.Value = inventoryItem.Quantity;
            }
            else if (inventoryItem.ClassId == Inventory.POISON_ANTIDOTE)
            {
                nudPoisonAntidote.Value = inventoryItem.Quantity;
            }
            else if (inventoryItem.ClassId == Inventory.SMALL_MEDIPACK)
            {
                nudSmallMedipack.Value = inventoryItem.Quantity;
            }

            // -------------- AMMO -------------- //
            else if (inventoryItem.ClassId == Inventory.DART_SS_AMMO)
            {
                nudDartSSAmmo.Value = inventoryItem.Quantity;
            }
            else if (inventoryItem.ClassId == Inventory.DESERT_RANGER_AMMO)
            {
                nudDesertRangerAmmo.Value = inventoryItem.Quantity;
            }
            else if (inventoryItem.ClassId == Inventory.MV9_AMMO)
            {
                nudMV9Ammo.Value = inventoryItem.Quantity;
            }
            else if (inventoryItem.ClassId == Inventory.RIGG_09_AMMO)
            {
                nudRigg09Ammo.Value = inventoryItem.Quantity;
            }
            else if (inventoryItem.ClassId == Inventory.VECTOR_R35_AMMO)
            {
                nudVectorR35Ammo.Value = inventoryItem.Quantity;
            }
            else if (inventoryItem.ClassId == Inventory.VPACKER_AMMO)
            {
                nudVPackerAmmo.Value = inventoryItem.Quantity;
            }
            else if (inventoryItem.ClassId == Inventory.VIPER_SMG_AMMO)
            {
                nudViperSMGAmmo.Value = inventoryItem.Quantity;
            }
            else if (inventoryItem.ClassId == Inventory.MAG_VEGA_AMMO)
            {
                nudMagVegaAmmo.Value = inventoryItem.Quantity;
            }
            else if (inventoryItem.ClassId == Inventory.K2_IMPACTOR_AMMO)
            {
                nudK2ImpactorAmmo.Value = inventoryItem.Quantity;
            }
            else if (inventoryItem.ClassId == Inventory.BORAN_X_AMMO)
            {
                nudBoranXAmmo.Value = inventoryItem.Quantity;
            }
            else if (inventoryItem.ClassId == Inventory.SCORPION_X_AMMO)
            {
                nudScorpionXAmmo.Value = inventoryItem.Quantity;
            }

            // -------------- WEAPONS -------------- //
            else if (inventoryItem.ClassId == Inventory.SCORPION_X)
            {
                chkScorpionX.Checked = true;
            }
            else if (inventoryItem.ClassId == Inventory.DART_SS)
            {
                chkDartSS.Checked = true;
            }
            else if (inventoryItem.ClassId == Inventory.DESERT_RANGER)
            {
                chkDesertRanger.Checked = true;
            }
            else if (inventoryItem.ClassId == Inventory.MV9)
            {
                chkMV9.Checked = true;
            }
            else if (inventoryItem.ClassId == Inventory.RIGG_09)
            {
                chkRigg09.Checked = true;
            }
            else if (inventoryItem.ClassId == Inventory.K2_IMPACTOR)
            {
                chkK2Impactor.Checked = true;
            }
            else if (inventoryItem.ClassId == Inventory.VECTOR_R35)
            {
                chkVectorR35.Checked = true;
            }
            else if (inventoryItem.ClassId == Inventory.VECTOR_R35_PAIR)
            {
                chkVectorR35Pair.Checked = true;
            }
            else if (inventoryItem.ClassId == Inventory.VPACKER)
            {
                chkVPacker.Checked = true;
            }
            else if (inventoryItem.ClassId == Inventory.VIPER_SMG)
            {
                chkViperSMG.Checked = true;
            }
            else if (inventoryItem.ClassId == Inventory.MAG_VEGA)
            {
                chkMagVega.Checked = true;
            }
            else if (inventoryItem.ClassId == Inventory.BORAN_X)
            {
                chkBoranX.Checked = true;
            }
            else if (inventoryItem.ClassId == Inventory.SCORPION_X_PAIR)
            {
                chkScorpionXPair.Checked = true;
            }
        }

        private void InvLoad2(BinaryReader reader)
        {
            Debug.WriteLine($"InvLoad2 Start: 0x{sgBufferCursor.ToString("X")}");
            Debug.WriteLine("");

            INVENTORY_START_OFFSET = sgBufferCursor;

            invLara.Clear();
            invKurtis.Clear();

            Int32 itemType;
            ushort itemClassID;
            Int32 itemCount;
            Int32 itemQuantity;

            int invActive;
            int characterIndex = 0;

            // Loop through inventory for Lara and Kent
            do
            {
                invActive = characterIndex;

                // Read the number of items in the current category (local_28)
                reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                itemCount = reader.ReadByte();
                sgBufferCursor += 1;

                Debug.WriteLine($"{(characterIndex == 0 ? "Lara" : "Kurtis")} Inventory Item Count = {itemCount}");

                // If there are items for this character, process each one
                if (itemCount != 0)
                {
                    int currentItemIndex = 0;

                    do
                    {
                        // Read item class ID (local_24)
                        reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                        itemClassID = reader.ReadUInt16();
                        sgBufferCursor += 2;

                        // Read item type (local_20)
                        reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                        itemType = reader.ReadInt32();
                        sgBufferCursor += 4;

                        // Read item quantity (local_1c)
                        reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                        itemQuantity = reader.ReadInt32();
                        sgBufferCursor += 4;

                        //Debug.WriteLine($"Item: ClassID=0x{itemClassID.ToString("X")}, Type={itemType}, Quantity={itemQuantity}, Quantity_Offset=0x{(sgBufferCursor - 4).ToString("X")}");

                        InventoryItem inventoryItem = new InventoryItem(itemClassID, itemType, itemQuantity);

                        if (characterIndex == 0)
                        {
                            invLara.Add(inventoryItem);
                        }
                        else
                        {
                            invKurtis.Add(inventoryItem);
                        }

                        currentItemIndex++;
                    } while (currentItemIndex < itemCount);
                }

                characterIndex++;
            } while (characterIndex < 2);

            //Debug.WriteLine($"invLoad2 End = 0x{sgBufferCursor:X}");

            INVENTORY_END_OFFSET = sgBufferCursor;
            PLAYER_HEALTH_OFFSET_2 = sgBufferCursor;

            int structuredSize = 0x4 + 0x4 + 0x4 + 0x1 + 0x1;   // Post-inventory data, includes player health

            reader.BaseStream.Seek(INVENTORY_END_OFFSET, SeekOrigin.Begin);
            postInventoryStructuredBlock = reader.ReadBytes(structuredSize);

            sgBufferCursor += structuredSize;

            POST_INVENTORY_END_OFFSET = sgBufferCursor;
        }

        private void CamLoad(BinaryReader reader)
        {
            FUN_00181ca8(reader);
            FUN_00181ca8(reader);
            FUN_00181ca8(reader);

            reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
            Int32 bInStealthWallMode = reader.ReadInt32();
            sgBufferCursor += 4;

            reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
            Int32 gChaseCameraMode = reader.ReadInt32();
            sgBufferCursor += 4;

            Debug.WriteLine($"CamLoad End: 0x{sgBufferCursor.ToString("X")}");
        }

        private void FxLoad(BinaryReader reader)
        {
            reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
            ushort zbufferfog_mode = reader.ReadByte();
            sgBufferCursor += 1;

            if (zbufferfog_mode != 0)
            {
                reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                Int32 zbufferfog_count = reader.ReadInt32();
                sgBufferCursor += 4;

                reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                Int32 zbufferfog_fadetime = reader.ReadInt32();
                sgBufferCursor += 4;

                reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                Int32 zbufferfog_turnoff = reader.ReadInt32();
                sgBufferCursor += 4;

                reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                ushort zbufferfog_R = reader.ReadByte();
                sgBufferCursor += 1;

                reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                ushort zbufferfog_G = reader.ReadByte();
                sgBufferCursor += 1;

                reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                ushort zbufferfog_B = reader.ReadByte();
                sgBufferCursor += 1;

                reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                ushort zbufferfog_SR = reader.ReadByte();
                sgBufferCursor += 1;

                reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                ushort zbufferfog_SG = reader.ReadByte();
                sgBufferCursor += 1;

                reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                ushort zbufferfog_SB = reader.ReadByte();
                sgBufferCursor += 1;

                reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                ushort zbufferfog_DR = reader.ReadByte();
                sgBufferCursor += 1;

                reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                ushort zbufferfog_DG = reader.ReadByte();
                sgBufferCursor += 1;

                reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                ushort zbufferfog_DB = reader.ReadByte();
                sgBufferCursor += 1;

                reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                Int32 zbuffer_range = reader.ReadInt32();
                sgBufferCursor += 4;

                reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                Int32 zbufferfog_Srange = reader.ReadInt32();
                sgBufferCursor += 4;

                reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                Int32 zbufferfog_Drange = reader.ReadInt32();
                sgBufferCursor += 4;

                reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                byte[] zbufferfog_clut = reader.ReadBytes(256);
                sgBufferCursor += 256;

                reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                byte[] zbufferfog_srcclut = reader.ReadBytes(256);
                sgBufferCursor += 256;

                reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                byte[] zbufferfog_dstclut = reader.ReadBytes(256);
                sgBufferCursor += 256;
            }

            reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
            Int32 fxSewerGushDeactivate = reader.ReadInt32();
            sgBufferCursor += 4;

            reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
            Int32 fxSnowActivate = reader.ReadInt32();
            sgBufferCursor += 4;

            Debug.WriteLine($"FxLoad End: 0x{sgBufferCursor.ToString("X")}");
        }

        private void FUN_00181ca8(BinaryReader reader)
        {
            reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
            reader.ReadBytes(0x10);
            sgBufferCursor += 0x10;

            reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
            reader.ReadBytes(0x10);
            sgBufferCursor += 0x10;

            reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
            reader.ReadInt32();
            sgBufferCursor += 4;

            reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
            reader.ReadInt32();
            sgBufferCursor += 4;

            reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
            reader.ReadBytes(0x10);
            sgBufferCursor += 0x10;

            reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
            reader.ReadInt32();
            sgBufferCursor += 4;

            reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
            reader.ReadInt32();
            sgBufferCursor += 4;

            reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
            reader.ReadInt32();
            sgBufferCursor += 4;

            //Debug.WriteLine($"FUN_00181ca8 End: 0x{sgBufferCursor.ToString("X")}");
        }

        private void AudioLoad(BinaryReader reader)
        {
            reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
            UInt16 gRestoreMusicStatus = reader.ReadUInt16();
            sgBufferCursor += 2;

            reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
            UInt16 gRestoreNextMusicRequest = reader.ReadUInt16();
            sgBufferCursor += 2;

            reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
            Int32 gRestoreMusicFadeInValue = reader.ReadInt32();
            sgBufferCursor += 4;

            reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
            Int32 gRestoreMusicFadeOutValue = reader.ReadInt32();
            sgBufferCursor += 4;

            reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
            Int32 gRestoreCurrentMusicEvent = reader.ReadInt32();
            sgBufferCursor += 4;

            reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
            Int32 gRestoreNextMusicEvent = reader.ReadInt32();
            sgBufferCursor += 4;

            //Debug.WriteLine($"gAudioSaveBufferLength offset = 0x{sgBufferCursor.ToString("X")}");

            reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
            Int32 gAudioSaveBufferLength = reader.ReadInt32();
            sgBufferCursor += 4;

            //Debug.WriteLine($"gAudioSaveBufferLength = 0x{gAudioSaveBufferLength.ToString("X")}");

            if ((gAudioSaveBufferLength == 0) || (0x2800 < gAudioSaveBufferLength))
            {
                gAudioSaveBufferLength = 0;
            }
            else
            {
                if (gAudioSaveBufferLength > 0)
                {
                    byte[] gAudioSaveBuffer = reader.ReadBytes(gAudioSaveBufferLength);
                    sgBufferCursor += gAudioSaveBufferLength;
                }
            }

            Debug.WriteLine($"AudioLoad End: 0x{sgBufferCursor.ToString("X")}");
        }

        private void MapPickupLoad(BinaryReader reader)
        {
            if (sgCurrentLevel == 4 || sgCurrentLevel == 5 || sgCurrentLevel == 6 ||
                sgCurrentLevel == 7 || sgCurrentLevel == 8 || sgCurrentLevel == 9 ||
                sgCurrentLevel == 10 || sgCurrentLevel == 0xB || sgCurrentLevel == 0xD ||
                sgCurrentLevel == 0x11 || sgCurrentLevel == 0x1E || sgCurrentLevel == 0x1F ||
                sgCurrentLevel == 0x20 || sgCurrentLevel == 0x21 || sgCurrentLevel == 0x22)
            {
                int gmapNumPickupLevels = 0xF;  // Seems to be static across all levels

                if (gmapNumPickupLevels > 0)
                {
                    int gmapPickupTags = 0;
                    int gmapPickupLevels = 0;
                    int index = 0;

                    do
                    {
                        // Read full 4-byte value but only use the lower 16 bits
                        reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                        int packedValue = reader.ReadInt32();
                        sgBufferCursor += 4;

                        gmapPickupLevels = packedValue & 0xFFFF;  // Extract ushort

                        //Debug.WriteLine($"gmapPickupLevels = 0x{gmapPickupLevels:X}, on Offset: 0x{(sgBufferCursor - 4):X}");

                        if (gmapPickupLevels != 0)
                        {
                            int index2 = 0;

                            do
                            {
                                // Read first pickup tag
                                reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                                int pickupTag1 = reader.ReadInt32();
                                sgBufferCursor += 4;

                                // Read second pickup tag
                                reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                                int pickupTag2 = reader.ReadInt32();
                                sgBufferCursor += 4;

                                index2++;
                            } while (index2 < gmapPickupLevels);
                        }

                        // Move to next pickup level
                        gmapPickupTags += 0x80;  // Adjust pointer increment
                        index++;
                    } while (index < gmapNumPickupLevels);
                }
            }

            Debug.WriteLine($"MapPickupLoad End: 0x{sgBufferCursor:X}");
        }

        private void BossLoad(BinaryReader reader)
        {
            int local_24;
            int local_20;
            int local_1c;
            int local_18;
            int local_14;
            int[] local_10 = new int[4];
            int local_c;
            int local_8;

            if ((sgCurrentLevel == 0x1B) || (sgCurrentLevel == 0x1C))
            {
                if (sgCurrentLevel == 0x1B)
                {
                    reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                    local_10[0] = reader.ReadInt32();
                    sgBufferCursor += 4;

                    reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                    local_c = reader.ReadInt32();
                    sgBufferCursor += 4;

                    reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                    local_8 = reader.ReadInt32();
                    sgBufferCursor += 4;

                    reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                    reader.ReadInt32();
                    sgBufferCursor += 4;

                    reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                    reader.ReadInt32();
                    sgBufferCursor += 4;

                    reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                    reader.ReadInt32();
                    sgBufferCursor += 4;

                    reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                    reader.ReadInt32();
                    sgBufferCursor += 4;

                    reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                    reader.ReadInt32();
                    sgBufferCursor += 4;

                    reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                    local_10[1] = reader.ReadInt32();
                    sgBufferCursor += 4;

                    reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                    local_c = reader.ReadInt32();
                    sgBufferCursor += 4;

                    reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                    local_8 = reader.ReadInt32();
                    sgBufferCursor += 4;

                    reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                    reader.ReadInt32();
                    sgBufferCursor += 4;

                    reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                    reader.ReadInt32();
                    sgBufferCursor += 4;

                    Debug.WriteLine($"BossLoad End: 0x{sgBufferCursor.ToString("X")}");
                    return;
                }
                if (sgCurrentLevel == 0x1C)
                {
                    reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                    local_24 = reader.ReadInt32();
                    sgBufferCursor += 4;

                    reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                    local_20 = reader.ReadInt32();
                    sgBufferCursor += 4;

                    reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                    local_1c = reader.ReadInt32();
                    sgBufferCursor += 4;

                    reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                    local_18 = reader.ReadInt32();
                    sgBufferCursor += 4;

                    reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                    local_14 = reader.ReadInt32();
                    sgBufferCursor += 4;

                    reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                    reader.ReadInt32();
                    sgBufferCursor += 4;

                    Debug.WriteLine($"BossLoad End: 0x{sgBufferCursor.ToString("X")}");
                    return;
                }
            }

            Debug.WriteLine($"BossLoad End: 0x{sgBufferCursor.ToString("X")}");
        }


        private void DisplaySavegameData(List<byte> decompressedBlock, List<byte> remainingBlock)
        {
            sgBufferCursor = 0;

            // Combine decompressedBlock and remainingBlock
            List<byte> savegameData = new List<byte>(decompressedBlock);
            savegameData.AddRange(remainingBlock);

            using (MemoryStream ms = new MemoryStream(savegameData.ToArray()))
            using (BinaryReader reader = new BinaryReader(ms))
            {
                // First read of sgCurrentLevel (1 byte)
                reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                sgCurrentLevel = reader.ReadByte();
                sgBufferCursor += 1;

                // Second read of sgCurrentLoadedZone (4 bytes)
                reader.BaseStream.Seek(sgBufferCursor, SeekOrigin.Begin);
                sgCurrentLoadedZone = reader.ReadInt32();
                sgBufferCursor += 4;

                mapName = mapNames[(byte)sgCurrentLevel];
                MapLoadGMX(mapName);

                FeLoad(reader);
                InvLoad(reader);
                MapLoad(reader);
                CamLoad(reader);
                FxLoad(reader);
                AudioLoad(reader);
                MapPickupLoad(reader);
                BossLoad(reader);
                InvLoad2(reader);
            }
        }

        private void EnableButtons()
        {
            btnCancel.Enabled = true;
            btnSave.Enabled = true;
        }

        private void DisableButtons()
        {
            btnCancel.Enabled = false;
            btnSave.Enabled = false;
        }

        private bool IsValidSavegame(string filePath)
        {
            try
            {
                if (filePath.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                using (FileStream saveFile = new FileStream(
                    filePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite))
                {
                    if (saveFile.Length < 4)
                    {
                        return false;
                    }


                    byte[] fileHeader = new byte[4];
                    saveFile.Read(fileHeader, 0, 4);

                    for (int i = 0; i < 4; i++)
                    {
                        if (fileHeader[i] != TOMB_SIGNATURE[i])
                        {
                            return false;
                        }

                    }
                }

                return true;
            }
            catch
            {
                return false; // file locked, corrupt, etc.
            }
        }

        private ushort GetCompressedBlockSize()
        {
            using (FileStream fs = new FileStream(
                currentSavegamePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite))
            {
                fs.Seek(COMPRESSED_BLOCK_SIZE_OFFSET, SeekOrigin.Begin);

                byte[] buffer = new byte[2];
                int bytesRead = fs.Read(buffer, 0, 2);

                if (bytesRead != 2)
                {
                    throw new Exception("Failed to read compressed block size.");
                }

                return BitConverter.ToUInt16(buffer, 0); // little-endian
            }
        }

        private byte GetHeaderLevelIndex(string filePath)
        {
            using (FileStream fs = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite))
            {
                fs.Seek(HEADER_LEVEL_INDEX_OFFSET, SeekOrigin.Begin);

                int value = fs.ReadByte();
                if (value == -1)
                {
                    throw new Exception("Failed to read level index.");
                }


                return (byte)value;
            }
        }

        private void CheckConfigFile()
        {
            string iniFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CONFIG_FILE_NAME);

            if (!File.Exists(iniFilePath))
            {
                using (StreamWriter sw = File.CreateText(iniFilePath))
                {
                    sw.WriteLine($"Path={(string.IsNullOrEmpty(savegameDirectory) ? "" : savegameDirectory)}");
                }
            }
            else
            {
                string[] lines = File.ReadAllLines(iniFilePath);
                foreach (string line in lines)
                {
                    if (line.StartsWith("Path="))
                    {
                        string pathValue = line.Substring(5).Trim();

                        if (!string.IsNullOrEmpty(pathValue) && Directory.Exists(pathValue))
                        {
                            savegameDirectory = $"{pathValue}\\SaveGame";
                            gameDirectory = pathValue;
                            txtDirectory.Text = gameDirectory;
                        }
                    }
                }
            }
        }

        private void UpdateConfigFile()
        {
            string iniFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CONFIG_FILE_NAME);

            if (File.Exists(iniFilePath))
            {
                string[] lines = File.ReadAllLines(iniFilePath);

                bool pathUpdated = false;

                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].StartsWith("Path="))
                    {
                        lines[i] = $"Path={gameDirectory}";
                        pathUpdated = true;
                        break;
                    }
                }

                if (!pathUpdated)
                {
                    var linesList = new List<string>(lines);
                    linesList.Add($"Path={gameDirectory}");
                    lines = linesList.ToArray();
                }

                File.WriteAllLines(iniFilePath, lines);
            }
        }

        private bool IsActorPlayable(EntityMock actor)
        {
            if (actor.ID == null)
            {
                Debug.WriteLine("Actor ID is null. Cannot query ActorDB.");
                return false;
            }

            // Query ActorDB for the actor's playability
            if (ActorDB.TryGetValue((int)actor.ID, out Actor actorData))
            {
                return actorData.IsPlayable;
            }
            else
            {
                Debug.WriteLine($"Actor ID 0x{actor.ID:X8} not found in ActorDB.");
                return false;
            }
        }

        private void ParseActorDB(string actorDbPath)
        {
            try
            {
                using (var reader = new BinaryReader(File.Open(actorDbPath, FileMode.Open, FileAccess.Read)))
                {
                    reader.BaseStream.Seek(0x4, SeekOrigin.Begin); // Skip header

                    int recordSize = 0x24;
                    while (reader.BaseStream.Position + recordSize <= reader.BaseStream.Length)
                    {
                        try
                        {
                            long recordStart = reader.BaseStream.Position;

                            // Read Actor ID (4 bytes)
                            int actorId = reader.ReadInt32();

                            // Skip 4 unknown bytes
                            reader.BaseStream.Seek(0x4, SeekOrigin.Current);

                            // Read Actor Type (4 bytes)
                            int actorType = reader.ReadInt32();

                            // Skip 4 unknown bytes
                            reader.BaseStream.Seek(0x4, SeekOrigin.Current);

                            // Read Actor Name (16 bytes, null-terminated string)
                            byte[] nameBytes = reader.ReadBytes(16);
                            string actorName = System.Text.Encoding.ASCII.GetString(nameBytes).Split('\0')[0];

                            // Determine if actor is playable
                            bool isPlayable = actorType == 1;

                            // Create Actor object
                            Actor actor = new Actor(actorId, actorName, isPlayable);

                            // Add actor to global ActorDB dictionary
                            if (!ActorDB.ContainsKey(actorId))
                            {
                                ActorDB[actorId] = actor;
                            }
                            else
                            {
                                Debug.WriteLine($"Warning: Duplicate Actor ID 0x{actorId:X} found in ACTOR.db. Skipping entry.");
                            }

                            // Debug output
                            Debug.WriteLine($"Parsed Actor -> ID: 0x{actorId:X}, Name: {actorName}, IsPlayable: {isPlayable}");

                            // Manually move to the next record start
                            reader.BaseStream.Seek(recordStart + recordSize, SeekOrigin.Begin);
                        }
                        catch (EndOfStreamException)
                        {
                            break; // Reached the end of the file
                        }
                    }
                }

                Debug.WriteLine($"Parsed {ActorDB.Count} unique actors from ACTOR.db.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error parsing ACTOR.db: {ex.Message}");
            }
        }

        private bool IsPlayerKurtis()
        {
            // The Sanitarium, Maximum Containment Area, Boaz Returns
            return sgCurrentLevel == 24 || sgCurrentLevel == 25 || sgCurrentLevel == 27;
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            UpdateConfigFile();
        }

        private void cmbSavegame_SelectedIndexChanged(object sender, EventArgs e)
        {
            ParseSavegameData(cmbSavegame.SelectedItem as Savegame);
            cmbInventory.SelectedIndex = IsPlayerKurtis() ? 1 : 0;
        }

        private void cmbSavegame_MouseDown(object sender, MouseEventArgs e)
        {
            cmbSavegame.SelectedIndexChanged -= cmbSavegame_SelectedIndexChanged;

            RefreshSavegames();

            cmbSavegame.SelectedIndexChanged += cmbSavegame_SelectedIndexChanged;
        }

        private void cmbInventory_SelectedIndexChanged(object sender, EventArgs e)
        {
            isInventoryLoading = true;

            if (!isLoading)
            {
                ResetInventoryDisplay();
            }

            List<InventoryItem> inventory = cmbInventory.SelectedIndex == 0 ? invLara : invKurtis;

            foreach (var item in inventory)
            {
                DisplayInventoryItem(item);
            }

            isInventoryLoading = false;
        }

        private void WriteChanges()
        {
            try
            {
                CreateBackup();

                Debug.WriteLine("========== WRITE START ==========");

                // -------------------------------------------------
                // Rebuild original full logical payload
                // -------------------------------------------------
                List<byte> savegameData = new List<byte>(decompressedBlock);
                savegameData.AddRange(remainingBlock);

                byte[] originalFullBuffer = savegameData.ToArray();

                if (originalFullBuffer.Length != PAYLOAD_SIZE)
                {
                    throw new Exception("Original logical buffer size invalid.");
                }

                Debug.WriteLine($"Full logical buffer size: 0x{originalFullBuffer.Length:X}");

                // -------------------------------------------------
                // Slice PRE
                // -------------------------------------------------
                byte[] preInventoryBlock = originalFullBuffer
                    .Take(INVENTORY_START_OFFSET)
                    .ToArray();

                int originalInventorySize = INVENTORY_END_OFFSET - INVENTORY_START_OFFSET;

                Debug.WriteLine($"preInventoryBlock: 0x{preInventoryBlock.Length:X}");
                Debug.WriteLine($"originalInventorySize: 0x{originalInventorySize:X}");

                float healthValue = trbHealth.Value;

                // -------------------------------------------------
                // Modify cash/health (inside prefix)
                // -------------------------------------------------
                using (var ms = new MemoryStream(preInventoryBlock))
                using (var bw = new BinaryWriter(ms))
                {
                    ms.Seek(PLAYER_CASH_OFFSET, SeekOrigin.Begin);
                    bw.Write((int)nudCash.Value);

                    if (trbHealth.Enabled)
                    {
                        ms.Seek(PLAYER_HEALTH_OFFSET, SeekOrigin.Begin);
                        byte[] healthBytes = BitConverter.GetBytes(healthValue);
                        bw.Write(healthBytes);
                    }
                }

                // -------------------------------------------------
                // Rebuild inv2
                // -------------------------------------------------
                byte[] newInventoryBlock;

                using (var invMs = new MemoryStream())
                using (var invWriter = new BinaryWriter(invMs))
                {
                    invWriter.Write((byte)invLara.Count);
                    foreach (var item in invLara)
                    {
                        invWriter.Write((UInt16)item.ClassId);
                        invWriter.Write((Int32)item.Type);
                        invWriter.Write((Int32)item.Quantity);
                    }

                    invWriter.Write((byte)invKurtis.Count);
                    foreach (var item in invKurtis)
                    {
                        invWriter.Write((UInt16)item.ClassId);
                        invWriter.Write((Int32)item.Type);
                        invWriter.Write((Int32)item.Quantity);
                    }

                    newInventoryBlock = invMs.ToArray();
                }

                Debug.WriteLine($"New inv2 size: 0x{newInventoryBlock.Length:X}");

                // -------------------------------------------------
                // Extract structured + raw tail separately
                // -------------------------------------------------
                int structuredSize = postInventoryStructuredBlock.Length;

                byte[] oldStructured =
                    originalFullBuffer
                    .Skip(INVENTORY_END_OFFSET)
                    .Take(structuredSize)
                    .ToArray();

                byte[] newStructured = (byte[])oldStructured.Clone();

                using (var ms = new MemoryStream(newStructured))
                using (var bw = new BinaryWriter(ms))
                {
                    if (trbHealth.Enabled)
                    {
                        // Write health at second position
                        bw.Write(healthValue);
                    }
                }

                byte[] oldRawTail =
                    originalFullBuffer
                    .Skip(INVENTORY_END_OFFSET + structuredSize)
                    .ToArray();

                Debug.WriteLine($"Structured block size: 0x{oldStructured.Length:X}");
                Debug.WriteLine($"Raw tail size:         0x{oldRawTail.Length:X}");

                // -------------------------------------------------
                // Elastic raw tail logic
                // -------------------------------------------------
                int delta = newInventoryBlock.Length - originalInventorySize;
                Debug.WriteLine($"Inventory delta: {delta}");

                byte[] newRawTail;

                if (delta > 0)
                {
                    if (delta > oldRawTail.Length)
                    {
                        throw new Exception("Inventory grew beyond available tail space.");
                    }

                    newRawTail = oldRawTail.Skip(delta).ToArray();
                }
                else if (delta < 0)
                {
                    int pad = -delta;
                    newRawTail = new byte[pad].Concat(oldRawTail).ToArray();
                }
                else
                {
                    newRawTail = oldRawTail;
                }

                Debug.WriteLine($"New raw tail size: 0x{newRawTail.Length:X}");

                // -------------------------------------------------
                // Rebuild full logical payload (fixed size)
                // -------------------------------------------------
                byte[] modifiedFullBuffer = preInventoryBlock
                    .Concat(newInventoryBlock)
                    .Concat(newStructured)
                    .Concat(newRawTail)
                    .ToArray();

                Debug.WriteLine($"Modified full logical buffer: 0x{modifiedFullBuffer.Length:X}");

                if (modifiedFullBuffer.Length != PAYLOAD_SIZE)
                {
                    throw new Exception($"Logical buffer size mismatch. Expected 0x{PAYLOAD_SIZE:X}, got 0x{modifiedFullBuffer.Length:X}");
                }

                // -------------------------------------------------
                // Split prefix / raw tail
                // -------------------------------------------------
                int logicalPrefixLen = GetCompressedBlockSize();

                byte[] prefixLogical = modifiedFullBuffer.Take(logicalPrefixLen).ToArray();
                byte[] rawTail = modifiedFullBuffer.Skip(logicalPrefixLen).ToArray();

                // -------------------------------------------------
                // Compress prefix
                // -------------------------------------------------
                byte[] compressedBuffer = Pack(prefixLogical);
                int compressedBytesLen = compressedBuffer.Length;

                Debug.WriteLine($"Compressed bytes length: 0x{compressedBytesLen:X}");

                if (compressedBytesLen > logicalPrefixLen)
                {
                    throw new Exception("Compressed data larger than logical prefix.");
                }

                // -------------------------------------------------
                // Write file
                // -------------------------------------------------
                using (FileStream fs = new FileStream(currentSavegamePath, FileMode.Open, FileAccess.Write))
                using (BinaryWriter writer = new BinaryWriter(fs))
                {
                    // Preserve logical prefix length
                    fs.Seek(COMPRESSED_BLOCK_SIZE_OFFSET, SeekOrigin.Begin);
                    writer.Write((UInt16)logicalPrefixLen);

                    // Write compressed prefix
                    fs.Seek(HEADER_SIZE, SeekOrigin.Begin);
                    writer.Write(compressedBuffer);

                    // Zero-pad unused prefix bytes
                    int padLen = logicalPrefixLen - compressedBytesLen;
                    if (padLen > 0)
                    {
                        writer.Write(new byte[padLen]);
                    }

                    // Write raw tail
                    fs.Seek(HEADER_SIZE + logicalPrefixLen, SeekOrigin.Begin);
                    writer.Write(rawTail);

                    // Update compressed-size field
                    fs.Seek(HEADER_SIZE + COMPRESSED_SIZE_FIELD_IN_PAYLOAD, SeekOrigin.Begin);
                    writer.Write((UInt32)compressedBytesLen);
                }

                Debug.WriteLine("========== WRITE END ==========");

                slblStatus.Text = "Successfully patched savegame";
            }
            catch (Exception ex)
            {
                slblStatus.Text = "Error patching savegame";
                throw new Exception($"Error while writing to buffer: {ex.Message}");
            }
        }

        private void CreateBackup()
        {
            if (string.IsNullOrEmpty(savegameDirectory) || !File.Exists(currentSavegamePath))
            {
                return;
            }

            string fileName = Path.GetFileName(currentSavegamePath);
            string backupFilePath = Path.Combine(savegameDirectory, fileName + ".bak");

            try
            {
                File.Copy(currentSavegamePath, backupFilePath, true);

                Debug.WriteLine($"Created savegame backup: \"{backupFilePath}\"");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Backup failed: {ex.Message}");
            }
        }

        public void UpdateInventoryFromUI(
            ComboBox cmbInventory,
            NumericUpDown nudChocolateBar,
            NumericUpDown nudHealthPills,
            CheckBox chkMV9, CheckBox chkVPacker,
            NumericUpDown nudMV9Ammo, NumericUpDown nudVPackerAmmo,
            CheckBox chkBoranX, NumericUpDown nudBoranXAmmo,
            NumericUpDown nudSmallMedipack,
            NumericUpDown nudHealthBandages,
            CheckBox chkK2Impactor, NumericUpDown nudK2ImpactorAmmo,
            NumericUpDown nudLargeHealthPack,
            CheckBox chkScorpionX, NumericUpDown nudScorpionXAmmo,
            CheckBox chkVectorR35, NumericUpDown nudVectorR35Ammo,
            CheckBox chkDesertRanger, NumericUpDown nudDesertRangerAmmo,
            CheckBox chkDartSS, NumericUpDown nudDartSSAmmo,
            CheckBox chkRigg09, NumericUpDown nudRigg09Ammo,
            CheckBox chkViperSMG, NumericUpDown nudViperSMGAmmo,
            CheckBox chkMagVega, NumericUpDown nudMagVegaAmmo,
            CheckBox chkVectorR35Pair, CheckBox chkScorpionXPair,
            NumericUpDown nudPoisonAntidote)
        {
            // Determine whose inventory to update
            List<InventoryItem> selectedInventory = cmbInventory.SelectedIndex == 1 ? invKurtis : invLara;

            // Weapons
            UpdateWeapon(selectedInventory, Inventory.MV9, chkMV9.Checked);
            UpdateWeapon(selectedInventory, Inventory.VPACKER, chkVPacker.Checked);
            UpdateWeapon(selectedInventory, Inventory.BORAN_X, chkBoranX.Checked);
            UpdateWeapon(selectedInventory, Inventory.K2_IMPACTOR, chkK2Impactor.Checked);
            UpdateWeapon(selectedInventory, Inventory.SCORPION_X, chkScorpionX.Checked);
            UpdateWeapon(selectedInventory, Inventory.VECTOR_R35, chkVectorR35.Checked);
            UpdateWeapon(selectedInventory, Inventory.DESERT_RANGER, chkDesertRanger.Checked);
            UpdateWeapon(selectedInventory, Inventory.DART_SS, chkDartSS.Checked);
            UpdateWeapon(selectedInventory, Inventory.RIGG_09, chkRigg09.Checked);
            UpdateWeapon(selectedInventory, Inventory.VIPER_SMG, chkViperSMG.Checked);
            UpdateWeapon(selectedInventory, Inventory.MAG_VEGA, chkMagVega.Checked);
            UpdateWeapon(selectedInventory, Inventory.SCORPION_X_PAIR, chkScorpionXPair.Checked);
            UpdateWeapon(selectedInventory, Inventory.VECTOR_R35_PAIR, chkVectorR35Pair.Checked);

            // Items
            UpdateItem(selectedInventory, Inventory.CHOCOLATE_BAR, (int)nudChocolateBar.Value);
            UpdateItem(selectedInventory, Inventory.SMALL_MEDIPACK, (int)nudSmallMedipack.Value);
            UpdateItem(selectedInventory, Inventory.HEALTH_BANDAGES, (int)nudHealthBandages.Value);
            UpdateItem(selectedInventory, Inventory.HEALTH_PILLS, (int)nudHealthPills.Value);
            UpdateItem(selectedInventory, Inventory.LARGE_HEALTH_PACK, (int)nudLargeHealthPack.Value);
            UpdateItem(selectedInventory, Inventory.POISON_ANTIDOTE, (int)nudPoisonAntidote.Value);

            // Ammo
            UpdateAmmo(selectedInventory, Inventory.MV9_AMMO, (int)nudMV9Ammo.Value);
            UpdateAmmo(selectedInventory, Inventory.VPACKER_AMMO, (int)nudVPackerAmmo.Value);
            UpdateAmmo(selectedInventory, Inventory.BORAN_X_AMMO, (int)nudBoranXAmmo.Value);
            UpdateAmmo(selectedInventory, Inventory.K2_IMPACTOR_AMMO, (int)nudK2ImpactorAmmo.Value);
            UpdateAmmo(selectedInventory, Inventory.SCORPION_X_AMMO, (int)nudScorpionXAmmo.Value);
            UpdateAmmo(selectedInventory, Inventory.VECTOR_R35_AMMO, (int)nudVectorR35Ammo.Value);
            UpdateAmmo(selectedInventory, Inventory.DESERT_RANGER_AMMO, (int)nudDesertRangerAmmo.Value);
            UpdateAmmo(selectedInventory, Inventory.DART_SS_AMMO, (int)nudDartSSAmmo.Value);
            UpdateAmmo(selectedInventory, Inventory.RIGG_09_AMMO, (int)nudRigg09Ammo.Value);
            UpdateAmmo(selectedInventory, Inventory.VIPER_SMG_AMMO, (int)nudViperSMGAmmo.Value);
            UpdateAmmo(selectedInventory, Inventory.MAG_VEGA_AMMO, (int)nudMagVegaAmmo.Value);
        }

        private void UpdateWeapon(List<InventoryItem> inventory, ushort classId, bool isChecked)
        {
            int index = inventory.FindIndex(i => i.ClassId == classId);

            if (isChecked)
            {
                if (index == -1) // Weapon does not exist, add it
                {
                    inventory.Add(new InventoryItem(classId, INVENTORY_TYPE_WEAPON, 1));
                }
            }
            else
            {
                if (index != -1) // Weapon exists, remove it
                {
                    inventory.RemoveAt(index);
                }
            }
        }

        private void UpdateItem(List<InventoryItem> inventory, ushort classId, int quantity)
        {
            // Find existing item
            int index = inventory.FindIndex(i => i.ClassId == classId);

            if (quantity > 0)
            {
                if (index != -1)
                {
                    inventory[index] = new InventoryItem(classId, INVENTORY_TYPE_ITEM, quantity); // Update existing item
                }
                else
                {
                    inventory.Add(new InventoryItem(classId, INVENTORY_TYPE_ITEM, quantity)); // Add new item
                }
            }
            else
            {
                if (index != -1)
                {
                    inventory.RemoveAt(index); // Remove if quantity is 0
                }
            }
        }

        private void UpdateAmmo(List<InventoryItem> inventory, ushort classId, int quantity)
        {
            // Find existing ammo item
            int index = inventory.FindIndex(i => i.ClassId == classId);

            if (quantity > 0)
            {
                if (index != -1)
                {
                    inventory[index] = new InventoryItem(classId, INVENTORY_TYPE_AMMO, quantity); // Update existing ammo
                }
                else
                {
                    inventory.Add(new InventoryItem(classId, INVENTORY_TYPE_AMMO, quantity)); // Add new ammo
                }
            }
            else
            {
                if (index != -1)
                {
                    inventory.RemoveAt(index); // Remove if quantity is 0
                }
            }
        }

        private void btnBrowse_Click(object sender, EventArgs e)
        {
            using (FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog())
            {
                folderBrowserDialog.Description = "Select your Tomb Raider: Angel of Darkness game directory";
                folderBrowserDialog.RootFolder = Environment.SpecialFolder.MyComputer;

                if (folderBrowserDialog.ShowDialog() == DialogResult.OK)
                {
                    savegameDirectory = $"{folderBrowserDialog.SelectedPath}\\SaveGame";
                    gameDirectory = folderBrowserDialog.SelectedPath;
                    txtDirectory.Text = gameDirectory;

                    LoadActorDB();
                    LoadSavegameList();
                }
            }
        }

        private void btnExit_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }

        private void btnAbout_Click(object sender, EventArgs e)
        {
            AboutForm aboutForm = new AboutForm();
            aboutForm.ShowDialog();
        }

        private void btnSave_Click(object sender, EventArgs e)
        {
            WriteChanges();
            DisableButtons();
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            Savegame selectedSavegame = cmbSavegame.SelectedItem as Savegame;

            if (selectedSavegame != null)
            {
                DisableButtons();

                cmbSavegame.SelectedIndexChanged -= cmbSavegame_SelectedIndexChanged;

                ParseSavegameData(cmbSavegame.SelectedItem as Savegame);

                cmbSavegame.SelectedIndexChanged += cmbSavegame_SelectedIndexChanged;
            }
        }

        private void trbHealth_Scroll(object sender, EventArgs e)
        {
            double healthPercentage = ((double)trbHealth.Value / (double)100) * 100;
            lblHealth.Text = $"{healthPercentage}%";

            if (!isLoading && cmbSavegame.SelectedIndex != -1)
            {
                EnableButtons();
            }
        }

        private void nudCash_ValueChanged(object sender, EventArgs e)
        {
            if (!isLoading && !isInventoryLoading && cmbInventory.SelectedIndex != -1)
            {
                UpdateInventoryFromUI(
                    cmbInventory,
                    nudChocolateBar,
                    nudHealthPills,
                    chkMV9, chkVPacker,
                    nudMV9Ammo, nudVPackerAmmo,
                    chkBoranX, nudBoranXAmmo,
                    nudSmallMedipack,
                    nudHealthBandages,
                    chkK2Impactor, nudK2ImpactorAmmo,
                    nudLargeHealthPack,
                    chkScorpionX, nudScorpionXAmmo,
                    chkVectorR35, nudVectorR35Ammo,
                    chkDesertRanger, nudDesertRangerAmmo,
                    chkDartSS, nudDartSSAmmo,
                    chkRigg09, nudRigg09Ammo,
                    chkViperSMG, nudViperSMGAmmo,
                    chkMagVega, nudMagVegaAmmo,
                    chkVectorR35Pair, chkScorpionXPair,
                    nudPoisonAntidote
                );

                EnableButtons();
            }
        }

        private void nudChocolateBar_ValueChanged(object sender, EventArgs e)
        {
            if (!isLoading && !isInventoryLoading && cmbInventory.SelectedIndex != -1)
            {
                UpdateInventoryFromUI(
                    cmbInventory,
                    nudChocolateBar,
                    nudHealthPills,
                    chkMV9, chkVPacker,
                    nudMV9Ammo, nudVPackerAmmo,
                    chkBoranX, nudBoranXAmmo,
                    nudSmallMedipack,
                    nudHealthBandages,
                    chkK2Impactor, nudK2ImpactorAmmo,
                    nudLargeHealthPack,
                    chkScorpionX, nudScorpionXAmmo,
                    chkVectorR35, nudVectorR35Ammo,
                    chkDesertRanger, nudDesertRangerAmmo,
                    chkDartSS, nudDartSSAmmo,
                    chkRigg09, nudRigg09Ammo,
                    chkViperSMG, nudViperSMGAmmo,
                    chkMagVega, nudMagVegaAmmo,
                    chkVectorR35Pair, chkScorpionXPair,
                    nudPoisonAntidote
                );

                EnableButtons();
            }
        }

        private void nudLargeHealthPack_ValueChanged(object sender, EventArgs e)
        {
            if (!isLoading && !isInventoryLoading && cmbInventory.SelectedIndex != -1)
            {
                UpdateInventoryFromUI(
                    cmbInventory,
                    nudChocolateBar,
                    nudHealthPills,
                    chkMV9, chkVPacker,
                    nudMV9Ammo, nudVPackerAmmo,
                    chkBoranX, nudBoranXAmmo,
                    nudSmallMedipack,
                    nudHealthBandages,
                    chkK2Impactor, nudK2ImpactorAmmo,
                    nudLargeHealthPack,
                    chkScorpionX, nudScorpionXAmmo,
                    chkVectorR35, nudVectorR35Ammo,
                    chkDesertRanger, nudDesertRangerAmmo,
                    chkDartSS, nudDartSSAmmo,
                    chkRigg09, nudRigg09Ammo,
                    chkViperSMG, nudViperSMGAmmo,
                    chkMagVega, nudMagVegaAmmo,
                    chkVectorR35Pair, chkScorpionXPair,
                    nudPoisonAntidote
                );

                EnableButtons();
            }
        }

        private void nudHealthBandages_ValueChanged(object sender, EventArgs e)
        {
            if (!isLoading && !isInventoryLoading && cmbInventory.SelectedIndex != -1)
            {
                UpdateInventoryFromUI(
                    cmbInventory,
                    nudChocolateBar,
                    nudHealthPills,
                    chkMV9, chkVPacker,
                    nudMV9Ammo, nudVPackerAmmo,
                    chkBoranX, nudBoranXAmmo,
                    nudSmallMedipack,
                    nudHealthBandages,
                    chkK2Impactor, nudK2ImpactorAmmo,
                    nudLargeHealthPack,
                    chkScorpionX, nudScorpionXAmmo,
                    chkVectorR35, nudVectorR35Ammo,
                    chkDesertRanger, nudDesertRangerAmmo,
                    chkDartSS, nudDartSSAmmo,
                    chkRigg09, nudRigg09Ammo,
                    chkViperSMG, nudViperSMGAmmo,
                    chkMagVega, nudMagVegaAmmo,
                    chkVectorR35Pair, chkScorpionXPair,
                    nudPoisonAntidote
                );

                EnableButtons();
            }
        }

        private void nudHealthPills_ValueChanged(object sender, EventArgs e)
        {
            if (!isLoading && !isInventoryLoading && cmbInventory.SelectedIndex != -1)
            {
                UpdateInventoryFromUI(
                    cmbInventory,
                    nudChocolateBar,
                    nudHealthPills,
                    chkMV9, chkVPacker,
                    nudMV9Ammo, nudVPackerAmmo,
                    chkBoranX, nudBoranXAmmo,
                    nudSmallMedipack,
                    nudHealthBandages,
                    chkK2Impactor, nudK2ImpactorAmmo,
                    nudLargeHealthPack,
                    chkScorpionX, nudScorpionXAmmo,
                    chkVectorR35, nudVectorR35Ammo,
                    chkDesertRanger, nudDesertRangerAmmo,
                    chkDartSS, nudDartSSAmmo,
                    chkRigg09, nudRigg09Ammo,
                    chkViperSMG, nudViperSMGAmmo,
                    chkMagVega, nudMagVegaAmmo,
                    chkVectorR35Pair, chkScorpionXPair,
                    nudPoisonAntidote
                );

                EnableButtons();
            }
        }

        private void nudSmallMedipack_ValueChanged(object sender, EventArgs e)
        {
            if (!isLoading && !isInventoryLoading && cmbInventory.SelectedIndex != -1)
            {
                UpdateInventoryFromUI(
                    cmbInventory,
                    nudChocolateBar,
                    nudHealthPills,
                    chkMV9, chkVPacker,
                    nudMV9Ammo, nudVPackerAmmo,
                    chkBoranX, nudBoranXAmmo,
                    nudSmallMedipack,
                    nudHealthBandages,
                    chkK2Impactor, nudK2ImpactorAmmo,
                    nudLargeHealthPack,
                    chkScorpionX, nudScorpionXAmmo,
                    chkVectorR35, nudVectorR35Ammo,
                    chkDesertRanger, nudDesertRangerAmmo,
                    chkDartSS, nudDartSSAmmo,
                    chkRigg09, nudRigg09Ammo,
                    chkViperSMG, nudViperSMGAmmo,
                    chkMagVega, nudMagVegaAmmo,
                    chkVectorR35Pair, chkScorpionXPair,
                    nudPoisonAntidote
                );

                EnableButtons();
            }
        }

        private void nudPoisonAntidote_ValueChanged(object sender, EventArgs e)
        {
            if (!isLoading && !isInventoryLoading && cmbInventory.SelectedIndex != -1)
            {
                UpdateInventoryFromUI(
                    cmbInventory,
                    nudChocolateBar,
                    nudHealthPills,
                    chkMV9, chkVPacker,
                    nudMV9Ammo, nudVPackerAmmo,
                    chkBoranX, nudBoranXAmmo,
                    nudSmallMedipack,
                    nudHealthBandages,
                    chkK2Impactor, nudK2ImpactorAmmo,
                    nudLargeHealthPack,
                    chkScorpionX, nudScorpionXAmmo,
                    chkVectorR35, nudVectorR35Ammo,
                    chkDesertRanger, nudDesertRangerAmmo,
                    chkDartSS, nudDartSSAmmo,
                    chkRigg09, nudRigg09Ammo,
                    chkViperSMG, nudViperSMGAmmo,
                    chkMagVega, nudMagVegaAmmo,
                    chkVectorR35Pair, chkScorpionXPair,
                    nudPoisonAntidote
                );

                EnableButtons();
            }
        }

        private void chkMV9_CheckedChanged(object sender, EventArgs e)
        {
            if (!isLoading && !isInventoryLoading && cmbInventory.SelectedIndex != -1)
            {
                UpdateInventoryFromUI(
                    cmbInventory,
                    nudChocolateBar,
                    nudHealthPills,
                    chkMV9, chkVPacker,
                    nudMV9Ammo, nudVPackerAmmo,
                    chkBoranX, nudBoranXAmmo,
                    nudSmallMedipack,
                    nudHealthBandages,
                    chkK2Impactor, nudK2ImpactorAmmo,
                    nudLargeHealthPack,
                    chkScorpionX, nudScorpionXAmmo,
                    chkVectorR35, nudVectorR35Ammo,
                    chkDesertRanger, nudDesertRangerAmmo,
                    chkDartSS, nudDartSSAmmo,
                    chkRigg09, nudRigg09Ammo,
                    chkViperSMG, nudViperSMGAmmo,
                    chkMagVega, nudMagVegaAmmo,
                    chkVectorR35Pair, chkScorpionXPair,
                    nudPoisonAntidote
                );

                EnableButtons();
            }
        }

        private void chkVPacker_CheckedChanged(object sender, EventArgs e)
        {
            if (!isLoading && !isInventoryLoading && cmbInventory.SelectedIndex != -1)
            {
                UpdateInventoryFromUI(
                    cmbInventory,
                    nudChocolateBar,
                    nudHealthPills,
                    chkMV9, chkVPacker,
                    nudMV9Ammo, nudVPackerAmmo,
                    chkBoranX, nudBoranXAmmo,
                    nudSmallMedipack,
                    nudHealthBandages,
                    chkK2Impactor, nudK2ImpactorAmmo,
                    nudLargeHealthPack,
                    chkScorpionX, nudScorpionXAmmo,
                    chkVectorR35, nudVectorR35Ammo,
                    chkDesertRanger, nudDesertRangerAmmo,
                    chkDartSS, nudDartSSAmmo,
                    chkRigg09, nudRigg09Ammo,
                    chkViperSMG, nudViperSMGAmmo,
                    chkMagVega, nudMagVegaAmmo,
                    chkVectorR35Pair, chkScorpionXPair,
                    nudPoisonAntidote
                );

                EnableButtons();
            }
        }

        private void chkVectorR35_CheckedChanged(object sender, EventArgs e)
        {
            if (!isLoading && !isInventoryLoading && cmbInventory.SelectedIndex != -1)
            {
                UpdateInventoryFromUI(
                    cmbInventory,
                    nudChocolateBar,
                    nudHealthPills,
                    chkMV9, chkVPacker,
                    nudMV9Ammo, nudVPackerAmmo,
                    chkBoranX, nudBoranXAmmo,
                    nudSmallMedipack,
                    nudHealthBandages,
                    chkK2Impactor, nudK2ImpactorAmmo,
                    nudLargeHealthPack,
                    chkScorpionX, nudScorpionXAmmo,
                    chkVectorR35, nudVectorR35Ammo,
                    chkDesertRanger, nudDesertRangerAmmo,
                    chkDartSS, nudDartSSAmmo,
                    chkRigg09, nudRigg09Ammo,
                    chkViperSMG, nudViperSMGAmmo,
                    chkMagVega, nudMagVegaAmmo,
                    chkVectorR35Pair, chkScorpionXPair,
                    nudPoisonAntidote
                );

                EnableButtons();
            }
        }

        private void chkVectorR35Pair_CheckedChanged(object sender, EventArgs e)
        {
            if (!isLoading && !isInventoryLoading && cmbInventory.SelectedIndex != -1)
            {
                UpdateInventoryFromUI(
                    cmbInventory,
                    nudChocolateBar,
                    nudHealthPills,
                    chkMV9, chkVPacker,
                    nudMV9Ammo, nudVPackerAmmo,
                    chkBoranX, nudBoranXAmmo,
                    nudSmallMedipack,
                    nudHealthBandages,
                    chkK2Impactor, nudK2ImpactorAmmo,
                    nudLargeHealthPack,
                    chkScorpionX, nudScorpionXAmmo,
                    chkVectorR35, nudVectorR35Ammo,
                    chkDesertRanger, nudDesertRangerAmmo,
                    chkDartSS, nudDartSSAmmo,
                    chkRigg09, nudRigg09Ammo,
                    chkViperSMG, nudViperSMGAmmo,
                    chkMagVega, nudMagVegaAmmo,
                    chkVectorR35Pair, chkScorpionXPair,
                    nudPoisonAntidote
                );

                EnableButtons();
            }
        }

        private void chkDesertRanger_CheckedChanged(object sender, EventArgs e)
        {
            if (!isLoading && !isInventoryLoading && cmbInventory.SelectedIndex != -1)
            {
                UpdateInventoryFromUI(
                    cmbInventory,
                    nudChocolateBar,
                    nudHealthPills,
                    chkMV9, chkVPacker,
                    nudMV9Ammo, nudVPackerAmmo,
                    chkBoranX, nudBoranXAmmo,
                    nudSmallMedipack,
                    nudHealthBandages,
                    chkK2Impactor, nudK2ImpactorAmmo,
                    nudLargeHealthPack,
                    chkScorpionX, nudScorpionXAmmo,
                    chkVectorR35, nudVectorR35Ammo,
                    chkDesertRanger, nudDesertRangerAmmo,
                    chkDartSS, nudDartSSAmmo,
                    chkRigg09, nudRigg09Ammo,
                    chkViperSMG, nudViperSMGAmmo,
                    chkMagVega, nudMagVegaAmmo,
                    chkVectorR35Pair, chkScorpionXPair,
                    nudPoisonAntidote
                );

                EnableButtons();
            }
        }

        private void chkDartSS_CheckedChanged(object sender, EventArgs e)
        {
            if (!isLoading && !isInventoryLoading && cmbInventory.SelectedIndex != -1)
            {
                UpdateInventoryFromUI(
                    cmbInventory,
                    nudChocolateBar,
                    nudHealthPills,
                    chkMV9, chkVPacker,
                    nudMV9Ammo, nudVPackerAmmo,
                    chkBoranX, nudBoranXAmmo,
                    nudSmallMedipack,
                    nudHealthBandages,
                    chkK2Impactor, nudK2ImpactorAmmo,
                    nudLargeHealthPack,
                    chkScorpionX, nudScorpionXAmmo,
                    chkVectorR35, nudVectorR35Ammo,
                    chkDesertRanger, nudDesertRangerAmmo,
                    chkDartSS, nudDartSSAmmo,
                    chkRigg09, nudRigg09Ammo,
                    chkViperSMG, nudViperSMGAmmo,
                    chkMagVega, nudMagVegaAmmo,
                    chkVectorR35Pair, chkScorpionXPair,
                    nudPoisonAntidote
                );

                EnableButtons();
            }
        }

        private void chkK2Impactor_CheckedChanged(object sender, EventArgs e)
        {
            if (!isLoading && !isInventoryLoading && cmbInventory.SelectedIndex != -1)
            {
                UpdateInventoryFromUI(
                    cmbInventory,
                    nudChocolateBar,
                    nudHealthPills,
                    chkMV9, chkVPacker,
                    nudMV9Ammo, nudVPackerAmmo,
                    chkBoranX, nudBoranXAmmo,
                    nudSmallMedipack,
                    nudHealthBandages,
                    chkK2Impactor, nudK2ImpactorAmmo,
                    nudLargeHealthPack,
                    chkScorpionX, nudScorpionXAmmo,
                    chkVectorR35, nudVectorR35Ammo,
                    chkDesertRanger, nudDesertRangerAmmo,
                    chkDartSS, nudDartSSAmmo,
                    chkRigg09, nudRigg09Ammo,
                    chkViperSMG, nudViperSMGAmmo,
                    chkMagVega, nudMagVegaAmmo,
                    chkVectorR35Pair, chkScorpionXPair,
                    nudPoisonAntidote
                );

                EnableButtons();
            }
        }

        private void chkRigg09_CheckedChanged(object sender, EventArgs e)
        {
            if (!isLoading && !isInventoryLoading && cmbInventory.SelectedIndex != -1)
            {
                UpdateInventoryFromUI(
                    cmbInventory,
                    nudChocolateBar,
                    nudHealthPills,
                    chkMV9, chkVPacker,
                    nudMV9Ammo, nudVPackerAmmo,
                    chkBoranX, nudBoranXAmmo,
                    nudSmallMedipack,
                    nudHealthBandages,
                    chkK2Impactor, nudK2ImpactorAmmo,
                    nudLargeHealthPack,
                    chkScorpionX, nudScorpionXAmmo,
                    chkVectorR35, nudVectorR35Ammo,
                    chkDesertRanger, nudDesertRangerAmmo,
                    chkDartSS, nudDartSSAmmo,
                    chkRigg09, nudRigg09Ammo,
                    chkViperSMG, nudViperSMGAmmo,
                    chkMagVega, nudMagVegaAmmo,
                    chkVectorR35Pair, chkScorpionXPair,
                    nudPoisonAntidote
                );

                EnableButtons();
            }
        }

        private void chkViperSMG_CheckedChanged(object sender, EventArgs e)
        {
            if (!isLoading && !isInventoryLoading && cmbInventory.SelectedIndex != -1)
            {
                UpdateInventoryFromUI(
                    cmbInventory,
                    nudChocolateBar,
                    nudHealthPills,
                    chkMV9, chkVPacker,
                    nudMV9Ammo, nudVPackerAmmo,
                    chkBoranX, nudBoranXAmmo,
                    nudSmallMedipack,
                    nudHealthBandages,
                    chkK2Impactor, nudK2ImpactorAmmo,
                    nudLargeHealthPack,
                    chkScorpionX, nudScorpionXAmmo,
                    chkVectorR35, nudVectorR35Ammo,
                    chkDesertRanger, nudDesertRangerAmmo,
                    chkDartSS, nudDartSSAmmo,
                    chkRigg09, nudRigg09Ammo,
                    chkViperSMG, nudViperSMGAmmo,
                    chkMagVega, nudMagVegaAmmo,
                    chkVectorR35Pair, chkScorpionXPair,
                    nudPoisonAntidote
                );

                EnableButtons();
            }
        }

        private void chkScorpionX_CheckedChanged(object sender, EventArgs e)
        {
            if (!isLoading && !isInventoryLoading && cmbInventory.SelectedIndex != -1)
            {
                UpdateInventoryFromUI(
                    cmbInventory,
                    nudChocolateBar,
                    nudHealthPills,
                    chkMV9, chkVPacker,
                    nudMV9Ammo, nudVPackerAmmo,
                    chkBoranX, nudBoranXAmmo,
                    nudSmallMedipack,
                    nudHealthBandages,
                    chkK2Impactor, nudK2ImpactorAmmo,
                    nudLargeHealthPack,
                    chkScorpionX, nudScorpionXAmmo,
                    chkVectorR35, nudVectorR35Ammo,
                    chkDesertRanger, nudDesertRangerAmmo,
                    chkDartSS, nudDartSSAmmo,
                    chkRigg09, nudRigg09Ammo,
                    chkViperSMG, nudViperSMGAmmo,
                    chkMagVega, nudMagVegaAmmo,
                    chkVectorR35Pair, chkScorpionXPair,
                    nudPoisonAntidote
                );

                EnableButtons();
            }
        }

        private void chkScorpionXPair_CheckedChanged(object sender, EventArgs e)
        {
            if (!isLoading && !isInventoryLoading && cmbInventory.SelectedIndex != -1)
            {
                UpdateInventoryFromUI(
                    cmbInventory,
                    nudChocolateBar,
                    nudHealthPills,
                    chkMV9, chkVPacker,
                    nudMV9Ammo, nudVPackerAmmo,
                    chkBoranX, nudBoranXAmmo,
                    nudSmallMedipack,
                    nudHealthBandages,
                    chkK2Impactor, nudK2ImpactorAmmo,
                    nudLargeHealthPack,
                    chkScorpionX, nudScorpionXAmmo,
                    chkVectorR35, nudVectorR35Ammo,
                    chkDesertRanger, nudDesertRangerAmmo,
                    chkDartSS, nudDartSSAmmo,
                    chkRigg09, nudRigg09Ammo,
                    chkViperSMG, nudViperSMGAmmo,
                    chkMagVega, nudMagVegaAmmo,
                    chkVectorR35Pair, chkScorpionXPair,
                    nudPoisonAntidote
                );

                EnableButtons();
            }
        }

        private void chkMagVega_CheckedChanged(object sender, EventArgs e)
        {
            if (!isLoading && !isInventoryLoading && cmbInventory.SelectedIndex != -1)
            {
                UpdateInventoryFromUI(
                    cmbInventory,
                    nudChocolateBar,
                    nudHealthPills,
                    chkMV9, chkVPacker,
                    nudMV9Ammo, nudVPackerAmmo,
                    chkBoranX, nudBoranXAmmo,
                    nudSmallMedipack,
                    nudHealthBandages,
                    chkK2Impactor, nudK2ImpactorAmmo,
                    nudLargeHealthPack,
                    chkScorpionX, nudScorpionXAmmo,
                    chkVectorR35, nudVectorR35Ammo,
                    chkDesertRanger, nudDesertRangerAmmo,
                    chkDartSS, nudDartSSAmmo,
                    chkRigg09, nudRigg09Ammo,
                    chkViperSMG, nudViperSMGAmmo,
                    chkMagVega, nudMagVegaAmmo,
                    chkVectorR35Pair, chkScorpionXPair,
                    nudPoisonAntidote
                );

                EnableButtons();
            }
        }

        private void chkBoranX_CheckedChanged(object sender, EventArgs e)
        {
            if (!isLoading && !isInventoryLoading && cmbInventory.SelectedIndex != -1)
            {
                UpdateInventoryFromUI(
                    cmbInventory,
                    nudChocolateBar,
                    nudHealthPills,
                    chkMV9, chkVPacker,
                    nudMV9Ammo, nudVPackerAmmo,
                    chkBoranX, nudBoranXAmmo,
                    nudSmallMedipack,
                    nudHealthBandages,
                    chkK2Impactor, nudK2ImpactorAmmo,
                    nudLargeHealthPack,
                    chkScorpionX, nudScorpionXAmmo,
                    chkVectorR35, nudVectorR35Ammo,
                    chkDesertRanger, nudDesertRangerAmmo,
                    chkDartSS, nudDartSSAmmo,
                    chkRigg09, nudRigg09Ammo,
                    chkViperSMG, nudViperSMGAmmo,
                    chkMagVega, nudMagVegaAmmo,
                    chkVectorR35Pair, chkScorpionXPair,
                    nudPoisonAntidote
                );

                EnableButtons();
            }
        }

        private void nudMV9Ammo_ValueChanged(object sender, EventArgs e)
        {
            if (!isLoading && !isInventoryLoading && cmbInventory.SelectedIndex != -1)
            {
                UpdateInventoryFromUI(
                    cmbInventory,
                    nudChocolateBar,
                    nudHealthPills,
                    chkMV9, chkVPacker,
                    nudMV9Ammo, nudVPackerAmmo,
                    chkBoranX, nudBoranXAmmo,
                    nudSmallMedipack,
                    nudHealthBandages,
                    chkK2Impactor, nudK2ImpactorAmmo,
                    nudLargeHealthPack,
                    chkScorpionX, nudScorpionXAmmo,
                    chkVectorR35, nudVectorR35Ammo,
                    chkDesertRanger, nudDesertRangerAmmo,
                    chkDartSS, nudDartSSAmmo,
                    chkRigg09, nudRigg09Ammo,
                    chkViperSMG, nudViperSMGAmmo,
                    chkMagVega, nudMagVegaAmmo,
                    chkVectorR35Pair, chkScorpionXPair,
                    nudPoisonAntidote
                );

                EnableButtons();
            }
        }

        private void nudVPackerAmmo_ValueChanged(object sender, EventArgs e)
        {
            if (!isLoading && !isInventoryLoading && cmbInventory.SelectedIndex != -1)
            {
                UpdateInventoryFromUI(
                    cmbInventory,
                    nudChocolateBar,
                    nudHealthPills,
                    chkMV9, chkVPacker,
                    nudMV9Ammo, nudVPackerAmmo,
                    chkBoranX, nudBoranXAmmo,
                    nudSmallMedipack,
                    nudHealthBandages,
                    chkK2Impactor, nudK2ImpactorAmmo,
                    nudLargeHealthPack,
                    chkScorpionX, nudScorpionXAmmo,
                    chkVectorR35, nudVectorR35Ammo,
                    chkDesertRanger, nudDesertRangerAmmo,
                    chkDartSS, nudDartSSAmmo,
                    chkRigg09, nudRigg09Ammo,
                    chkViperSMG, nudViperSMGAmmo,
                    chkMagVega, nudMagVegaAmmo,
                    chkVectorR35Pair, chkScorpionXPair,
                    nudPoisonAntidote
                );

                EnableButtons();
            }
        }

        private void nudVectorR35Ammo_ValueChanged(object sender, EventArgs e)
        {
            if (!isLoading && !isInventoryLoading && cmbInventory.SelectedIndex != -1)
            {
                UpdateInventoryFromUI(
                    cmbInventory,
                    nudChocolateBar,
                    nudHealthPills,
                    chkMV9, chkVPacker,
                    nudMV9Ammo, nudVPackerAmmo,
                    chkBoranX, nudBoranXAmmo,
                    nudSmallMedipack,
                    nudHealthBandages,
                    chkK2Impactor, nudK2ImpactorAmmo,
                    nudLargeHealthPack,
                    chkScorpionX, nudScorpionXAmmo,
                    chkVectorR35, nudVectorR35Ammo,
                    chkDesertRanger, nudDesertRangerAmmo,
                    chkDartSS, nudDartSSAmmo,
                    chkRigg09, nudRigg09Ammo,
                    chkViperSMG, nudViperSMGAmmo,
                    chkMagVega, nudMagVegaAmmo,
                    chkVectorR35Pair, chkScorpionXPair,
                    nudPoisonAntidote
                );

                EnableButtons();
            }
        }

        private void nudDesertRangerAmmo_ValueChanged(object sender, EventArgs e)
        {
            if (!isLoading && !isInventoryLoading && cmbInventory.SelectedIndex != -1)
            {
                UpdateInventoryFromUI(
                    cmbInventory,
                    nudChocolateBar,
                    nudHealthPills,
                    chkMV9, chkVPacker,
                    nudMV9Ammo, nudVPackerAmmo,
                    chkBoranX, nudBoranXAmmo,
                    nudSmallMedipack,
                    nudHealthBandages,
                    chkK2Impactor, nudK2ImpactorAmmo,
                    nudLargeHealthPack,
                    chkScorpionX, nudScorpionXAmmo,
                    chkVectorR35, nudVectorR35Ammo,
                    chkDesertRanger, nudDesertRangerAmmo,
                    chkDartSS, nudDartSSAmmo,
                    chkRigg09, nudRigg09Ammo,
                    chkViperSMG, nudViperSMGAmmo,
                    chkMagVega, nudMagVegaAmmo,
                    chkVectorR35Pair, chkScorpionXPair,
                    nudPoisonAntidote
                );

                EnableButtons();
            }
        }

        private void nudDartSSAmmo_ValueChanged(object sender, EventArgs e)
        {
            if (!isLoading && !isInventoryLoading && cmbInventory.SelectedIndex != -1)
            {
                UpdateInventoryFromUI(
                    cmbInventory,
                    nudChocolateBar,
                    nudHealthPills,
                    chkMV9, chkVPacker,
                    nudMV9Ammo, nudVPackerAmmo,
                    chkBoranX, nudBoranXAmmo,
                    nudSmallMedipack,
                    nudHealthBandages,
                    chkK2Impactor, nudK2ImpactorAmmo,
                    nudLargeHealthPack,
                    chkScorpionX, nudScorpionXAmmo,
                    chkVectorR35, nudVectorR35Ammo,
                    chkDesertRanger, nudDesertRangerAmmo,
                    chkDartSS, nudDartSSAmmo,
                    chkRigg09, nudRigg09Ammo,
                    chkViperSMG, nudViperSMGAmmo,
                    chkMagVega, nudMagVegaAmmo,
                    chkVectorR35Pair, chkScorpionXPair,
                    nudPoisonAntidote
                );

                EnableButtons();
            }
        }

        private void nudK2ImpactorAmmo_ValueChanged(object sender, EventArgs e)
        {
            if (!isLoading && !isInventoryLoading && cmbInventory.SelectedIndex != -1)
            {
                UpdateInventoryFromUI(
                    cmbInventory,
                    nudChocolateBar,
                    nudHealthPills,
                    chkMV9, chkVPacker,
                    nudMV9Ammo, nudVPackerAmmo,
                    chkBoranX, nudBoranXAmmo,
                    nudSmallMedipack,
                    nudHealthBandages,
                    chkK2Impactor, nudK2ImpactorAmmo,
                    nudLargeHealthPack,
                    chkScorpionX, nudScorpionXAmmo,
                    chkVectorR35, nudVectorR35Ammo,
                    chkDesertRanger, nudDesertRangerAmmo,
                    chkDartSS, nudDartSSAmmo,
                    chkRigg09, nudRigg09Ammo,
                    chkViperSMG, nudViperSMGAmmo,
                    chkMagVega, nudMagVegaAmmo,
                    chkVectorR35Pair, chkScorpionXPair,
                    nudPoisonAntidote
                );

                EnableButtons();
            }
        }

        private void nudRigg09Ammo_ValueChanged(object sender, EventArgs e)
        {
            if (!isLoading && !isInventoryLoading && cmbInventory.SelectedIndex != -1)
            {
                UpdateInventoryFromUI(
                    cmbInventory,
                    nudChocolateBar,
                    nudHealthPills,
                    chkMV9, chkVPacker,
                    nudMV9Ammo, nudVPackerAmmo,
                    chkBoranX, nudBoranXAmmo,
                    nudSmallMedipack,
                    nudHealthBandages,
                    chkK2Impactor, nudK2ImpactorAmmo,
                    nudLargeHealthPack,
                    chkScorpionX, nudScorpionXAmmo,
                    chkVectorR35, nudVectorR35Ammo,
                    chkDesertRanger, nudDesertRangerAmmo,
                    chkDartSS, nudDartSSAmmo,
                    chkRigg09, nudRigg09Ammo,
                    chkViperSMG, nudViperSMGAmmo,
                    chkMagVega, nudMagVegaAmmo,
                    chkVectorR35Pair, chkScorpionXPair,
                    nudPoisonAntidote
                );

                EnableButtons();
            }
        }

        private void nudViperSMGAmmo_ValueChanged(object sender, EventArgs e)
        {
            if (!isLoading && !isInventoryLoading && cmbInventory.SelectedIndex != -1)
            {
                UpdateInventoryFromUI(
                    cmbInventory,
                    nudChocolateBar,
                    nudHealthPills,
                    chkMV9, chkVPacker,
                    nudMV9Ammo, nudVPackerAmmo,
                    chkBoranX, nudBoranXAmmo,
                    nudSmallMedipack,
                    nudHealthBandages,
                    chkK2Impactor, nudK2ImpactorAmmo,
                    nudLargeHealthPack,
                    chkScorpionX, nudScorpionXAmmo,
                    chkVectorR35, nudVectorR35Ammo,
                    chkDesertRanger, nudDesertRangerAmmo,
                    chkDartSS, nudDartSSAmmo,
                    chkRigg09, nudRigg09Ammo,
                    chkViperSMG, nudViperSMGAmmo,
                    chkMagVega, nudMagVegaAmmo,
                    chkVectorR35Pair, chkScorpionXPair,
                    nudPoisonAntidote
                );

                EnableButtons();
            }
        }

        private void nudScorpionXAmmo_ValueChanged(object sender, EventArgs e)
        {
            if (!isLoading && !isInventoryLoading && cmbInventory.SelectedIndex != -1)
            {
                UpdateInventoryFromUI(
                    cmbInventory,
                    nudChocolateBar,
                    nudHealthPills,
                    chkMV9, chkVPacker,
                    nudMV9Ammo, nudVPackerAmmo,
                    chkBoranX, nudBoranXAmmo,
                    nudSmallMedipack,
                    nudHealthBandages,
                    chkK2Impactor, nudK2ImpactorAmmo,
                    nudLargeHealthPack,
                    chkScorpionX, nudScorpionXAmmo,
                    chkVectorR35, nudVectorR35Ammo,
                    chkDesertRanger, nudDesertRangerAmmo,
                    chkDartSS, nudDartSSAmmo,
                    chkRigg09, nudRigg09Ammo,
                    chkViperSMG, nudViperSMGAmmo,
                    chkMagVega, nudMagVegaAmmo,
                    chkVectorR35Pair, chkScorpionXPair,
                    nudPoisonAntidote
                );

                EnableButtons();
            }
        }

        private void nudMagVegaAmmo_ValueChanged(object sender, EventArgs e)
        {
            if (!isLoading && !isInventoryLoading && cmbInventory.SelectedIndex != -1)
            {
                UpdateInventoryFromUI(
                    cmbInventory,
                    nudChocolateBar,
                    nudHealthPills,
                    chkMV9, chkVPacker,
                    nudMV9Ammo, nudVPackerAmmo,
                    chkBoranX, nudBoranXAmmo,
                    nudSmallMedipack,
                    nudHealthBandages,
                    chkK2Impactor, nudK2ImpactorAmmo,
                    nudLargeHealthPack,
                    chkScorpionX, nudScorpionXAmmo,
                    chkVectorR35, nudVectorR35Ammo,
                    chkDesertRanger, nudDesertRangerAmmo,
                    chkDartSS, nudDartSSAmmo,
                    chkRigg09, nudRigg09Ammo,
                    chkViperSMG, nudViperSMGAmmo,
                    chkMagVega, nudMagVegaAmmo,
                    chkVectorR35Pair, chkScorpionXPair,
                    nudPoisonAntidote
                );

                EnableButtons();
            }
        }

        private void nudBoranXAmmo_ValueChanged(object sender, EventArgs e)
        {
            if (!isLoading && !isInventoryLoading && cmbInventory.SelectedIndex != -1)
            {
                UpdateInventoryFromUI(
                    cmbInventory,
                    nudChocolateBar,
                    nudHealthPills,
                    chkMV9, chkVPacker,
                    nudMV9Ammo, nudVPackerAmmo,
                    chkBoranX, nudBoranXAmmo,
                    nudSmallMedipack,
                    nudHealthBandages,
                    chkK2Impactor, nudK2ImpactorAmmo,
                    nudLargeHealthPack,
                    chkScorpionX, nudScorpionXAmmo,
                    chkVectorR35, nudVectorR35Ammo,
                    chkDesertRanger, nudDesertRangerAmmo,
                    chkDartSS, nudDartSSAmmo,
                    chkRigg09, nudRigg09Ammo,
                    chkViperSMG, nudViperSMGAmmo,
                    chkMagVega, nudMagVegaAmmo,
                    chkVectorR35Pair, chkScorpionXPair,
                    nudPoisonAntidote
                );

                EnableButtons();
            }
        }

        private void nudCash_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (!isLoading && !isInventoryLoading && cmbInventory.SelectedIndex != -1)
            {
                UpdateInventoryFromUI(
                    cmbInventory,
                    nudChocolateBar,
                    nudHealthPills,
                    chkMV9, chkVPacker,
                    nudMV9Ammo, nudVPackerAmmo,
                    chkBoranX, nudBoranXAmmo,
                    nudSmallMedipack,
                    nudHealthBandages,
                    chkK2Impactor, nudK2ImpactorAmmo,
                    nudLargeHealthPack,
                    chkScorpionX, nudScorpionXAmmo,
                    chkVectorR35, nudVectorR35Ammo,
                    chkDesertRanger, nudDesertRangerAmmo,
                    chkDartSS, nudDartSSAmmo,
                    chkRigg09, nudRigg09Ammo,
                    chkViperSMG, nudViperSMGAmmo,
                    chkMagVega, nudMagVegaAmmo,
                    chkVectorR35Pair, chkScorpionXPair,
                    nudPoisonAntidote
                );

                EnableButtons();
            }
        }

        private void nudChocolateBar_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (!isLoading && !isInventoryLoading && cmbInventory.SelectedIndex != -1)
            {
                UpdateInventoryFromUI(
                    cmbInventory,
                    nudChocolateBar,
                    nudHealthPills,
                    chkMV9, chkVPacker,
                    nudMV9Ammo, nudVPackerAmmo,
                    chkBoranX, nudBoranXAmmo,
                    nudSmallMedipack,
                    nudHealthBandages,
                    chkK2Impactor, nudK2ImpactorAmmo,
                    nudLargeHealthPack,
                    chkScorpionX, nudScorpionXAmmo,
                    chkVectorR35, nudVectorR35Ammo,
                    chkDesertRanger, nudDesertRangerAmmo,
                    chkDartSS, nudDartSSAmmo,
                    chkRigg09, nudRigg09Ammo,
                    chkViperSMG, nudViperSMGAmmo,
                    chkMagVega, nudMagVegaAmmo,
                    chkVectorR35Pair, chkScorpionXPair,
                    nudPoisonAntidote
                );

                EnableButtons();
            }
        }

        private void nudLargeHealthPack_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (!isLoading && !isInventoryLoading && cmbInventory.SelectedIndex != -1)
            {
                UpdateInventoryFromUI(
                    cmbInventory,
                    nudChocolateBar,
                    nudHealthPills,
                    chkMV9, chkVPacker,
                    nudMV9Ammo, nudVPackerAmmo,
                    chkBoranX, nudBoranXAmmo,
                    nudSmallMedipack,
                    nudHealthBandages,
                    chkK2Impactor, nudK2ImpactorAmmo,
                    nudLargeHealthPack,
                    chkScorpionX, nudScorpionXAmmo,
                    chkVectorR35, nudVectorR35Ammo,
                    chkDesertRanger, nudDesertRangerAmmo,
                    chkDartSS, nudDartSSAmmo,
                    chkRigg09, nudRigg09Ammo,
                    chkViperSMG, nudViperSMGAmmo,
                    chkMagVega, nudMagVegaAmmo,
                    chkVectorR35Pair, chkScorpionXPair,
                    nudPoisonAntidote
                );

                EnableButtons();
            }
        }

        private void nudHealthBandages_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (!isLoading && !isInventoryLoading && cmbInventory.SelectedIndex != -1)
            {
                UpdateInventoryFromUI(
                    cmbInventory,
                    nudChocolateBar,
                    nudHealthPills,
                    chkMV9, chkVPacker,
                    nudMV9Ammo, nudVPackerAmmo,
                    chkBoranX, nudBoranXAmmo,
                    nudSmallMedipack,
                    nudHealthBandages,
                    chkK2Impactor, nudK2ImpactorAmmo,
                    nudLargeHealthPack,
                    chkScorpionX, nudScorpionXAmmo,
                    chkVectorR35, nudVectorR35Ammo,
                    chkDesertRanger, nudDesertRangerAmmo,
                    chkDartSS, nudDartSSAmmo,
                    chkRigg09, nudRigg09Ammo,
                    chkViperSMG, nudViperSMGAmmo,
                    chkMagVega, nudMagVegaAmmo,
                    chkVectorR35Pair, chkScorpionXPair,
                    nudPoisonAntidote
                );

                EnableButtons();
            }
        }

        private void nudHealthPills_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (!isLoading && !isInventoryLoading && cmbInventory.SelectedIndex != -1)
            {
                UpdateInventoryFromUI(
                    cmbInventory,
                    nudChocolateBar,
                    nudHealthPills,
                    chkMV9, chkVPacker,
                    nudMV9Ammo, nudVPackerAmmo,
                    chkBoranX, nudBoranXAmmo,
                    nudSmallMedipack,
                    nudHealthBandages,
                    chkK2Impactor, nudK2ImpactorAmmo,
                    nudLargeHealthPack,
                    chkScorpionX, nudScorpionXAmmo,
                    chkVectorR35, nudVectorR35Ammo,
                    chkDesertRanger, nudDesertRangerAmmo,
                    chkDartSS, nudDartSSAmmo,
                    chkRigg09, nudRigg09Ammo,
                    chkViperSMG, nudViperSMGAmmo,
                    chkMagVega, nudMagVegaAmmo,
                    chkVectorR35Pair, chkScorpionXPair,
                    nudPoisonAntidote
                );

                EnableButtons();
            }
        }

        private void nudSmallMedipack_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (!isLoading && !isInventoryLoading && cmbInventory.SelectedIndex != -1)
            {
                UpdateInventoryFromUI(
                    cmbInventory,
                    nudChocolateBar,
                    nudHealthPills,
                    chkMV9, chkVPacker,
                    nudMV9Ammo, nudVPackerAmmo,
                    chkBoranX, nudBoranXAmmo,
                    nudSmallMedipack,
                    nudHealthBandages,
                    chkK2Impactor, nudK2ImpactorAmmo,
                    nudLargeHealthPack,
                    chkScorpionX, nudScorpionXAmmo,
                    chkVectorR35, nudVectorR35Ammo,
                    chkDesertRanger, nudDesertRangerAmmo,
                    chkDartSS, nudDartSSAmmo,
                    chkRigg09, nudRigg09Ammo,
                    chkViperSMG, nudViperSMGAmmo,
                    chkMagVega, nudMagVegaAmmo,
                    chkVectorR35Pair, chkScorpionXPair,
                    nudPoisonAntidote
                );

                EnableButtons();
            }
        }

        private void nudMV9Ammo_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (!isLoading && !isInventoryLoading && cmbInventory.SelectedIndex != -1)
            {
                UpdateInventoryFromUI(
                    cmbInventory,
                    nudChocolateBar,
                    nudHealthPills,
                    chkMV9, chkVPacker,
                    nudMV9Ammo, nudVPackerAmmo,
                    chkBoranX, nudBoranXAmmo,
                    nudSmallMedipack,
                    nudHealthBandages,
                    chkK2Impactor, nudK2ImpactorAmmo,
                    nudLargeHealthPack,
                    chkScorpionX, nudScorpionXAmmo,
                    chkVectorR35, nudVectorR35Ammo,
                    chkDesertRanger, nudDesertRangerAmmo,
                    chkDartSS, nudDartSSAmmo,
                    chkRigg09, nudRigg09Ammo,
                    chkViperSMG, nudViperSMGAmmo,
                    chkMagVega, nudMagVegaAmmo,
                    chkVectorR35Pair, chkScorpionXPair,
                    nudPoisonAntidote
                );

                EnableButtons();
            }
        }

        private void nudVPackerAmmo_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (!isLoading && !isInventoryLoading && cmbInventory.SelectedIndex != -1)
            {
                UpdateInventoryFromUI(
                    cmbInventory,
                    nudChocolateBar,
                    nudHealthPills,
                    chkMV9, chkVPacker,
                    nudMV9Ammo, nudVPackerAmmo,
                    chkBoranX, nudBoranXAmmo,
                    nudSmallMedipack,
                    nudHealthBandages,
                    chkK2Impactor, nudK2ImpactorAmmo,
                    nudLargeHealthPack,
                    chkScorpionX, nudScorpionXAmmo,
                    chkVectorR35, nudVectorR35Ammo,
                    chkDesertRanger, nudDesertRangerAmmo,
                    chkDartSS, nudDartSSAmmo,
                    chkRigg09, nudRigg09Ammo,
                    chkViperSMG, nudViperSMGAmmo,
                    chkMagVega, nudMagVegaAmmo,
                    chkVectorR35Pair, chkScorpionXPair,
                    nudPoisonAntidote
                );

                EnableButtons();
            }
        }

        private void nudVectorR35Ammo_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (!isLoading && !isInventoryLoading && cmbInventory.SelectedIndex != -1)
            {
                UpdateInventoryFromUI(
                    cmbInventory,
                    nudChocolateBar,
                    nudHealthPills,
                    chkMV9, chkVPacker,
                    nudMV9Ammo, nudVPackerAmmo,
                    chkBoranX, nudBoranXAmmo,
                    nudSmallMedipack,
                    nudHealthBandages,
                    chkK2Impactor, nudK2ImpactorAmmo,
                    nudLargeHealthPack,
                    chkScorpionX, nudScorpionXAmmo,
                    chkVectorR35, nudVectorR35Ammo,
                    chkDesertRanger, nudDesertRangerAmmo,
                    chkDartSS, nudDartSSAmmo,
                    chkRigg09, nudRigg09Ammo,
                    chkViperSMG, nudViperSMGAmmo,
                    chkMagVega, nudMagVegaAmmo,
                    chkVectorR35Pair, chkScorpionXPair,
                    nudPoisonAntidote
                );

                EnableButtons();
            }
        }

        private void nudDesertRangerAmmo_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (!isLoading && !isInventoryLoading && cmbInventory.SelectedIndex != -1)
            {
                UpdateInventoryFromUI(
                    cmbInventory,
                    nudChocolateBar,
                    nudHealthPills,
                    chkMV9, chkVPacker,
                    nudMV9Ammo, nudVPackerAmmo,
                    chkBoranX, nudBoranXAmmo,
                    nudSmallMedipack,
                    nudHealthBandages,
                    chkK2Impactor, nudK2ImpactorAmmo,
                    nudLargeHealthPack,
                    chkScorpionX, nudScorpionXAmmo,
                    chkVectorR35, nudVectorR35Ammo,
                    chkDesertRanger, nudDesertRangerAmmo,
                    chkDartSS, nudDartSSAmmo,
                    chkRigg09, nudRigg09Ammo,
                    chkViperSMG, nudViperSMGAmmo,
                    chkMagVega, nudMagVegaAmmo,
                    chkVectorR35Pair, chkScorpionXPair,
                    nudPoisonAntidote
                );

                EnableButtons();
            }
        }

        private void nudDartSSAmmo_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (!isLoading && !isInventoryLoading && cmbInventory.SelectedIndex != -1)
            {
                UpdateInventoryFromUI(
                    cmbInventory,
                    nudChocolateBar,
                    nudHealthPills,
                    chkMV9, chkVPacker,
                    nudMV9Ammo, nudVPackerAmmo,
                    chkBoranX, nudBoranXAmmo,
                    nudSmallMedipack,
                    nudHealthBandages,
                    chkK2Impactor, nudK2ImpactorAmmo,
                    nudLargeHealthPack,
                    chkScorpionX, nudScorpionXAmmo,
                    chkVectorR35, nudVectorR35Ammo,
                    chkDesertRanger, nudDesertRangerAmmo,
                    chkDartSS, nudDartSSAmmo,
                    chkRigg09, nudRigg09Ammo,
                    chkViperSMG, nudViperSMGAmmo,
                    chkMagVega, nudMagVegaAmmo,
                    chkVectorR35Pair, chkScorpionXPair,
                    nudPoisonAntidote
                );

                EnableButtons();
            }
        }

        private void nudK2ImpactorAmmo_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (!isLoading && !isInventoryLoading && cmbInventory.SelectedIndex != -1)
            {
                UpdateInventoryFromUI(
                    cmbInventory,
                    nudChocolateBar,
                    nudHealthPills,
                    chkMV9, chkVPacker,
                    nudMV9Ammo, nudVPackerAmmo,
                    chkBoranX, nudBoranXAmmo,
                    nudSmallMedipack,
                    nudHealthBandages,
                    chkK2Impactor, nudK2ImpactorAmmo,
                    nudLargeHealthPack,
                    chkScorpionX, nudScorpionXAmmo,
                    chkVectorR35, nudVectorR35Ammo,
                    chkDesertRanger, nudDesertRangerAmmo,
                    chkDartSS, nudDartSSAmmo,
                    chkRigg09, nudRigg09Ammo,
                    chkViperSMG, nudViperSMGAmmo,
                    chkMagVega, nudMagVegaAmmo,
                    chkVectorR35Pair, chkScorpionXPair,
                    nudPoisonAntidote
                );

                EnableButtons();
            }
        }

        private void nudRigg09Ammo_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (!isLoading && !isInventoryLoading && cmbInventory.SelectedIndex != -1)
            {
                UpdateInventoryFromUI(
                    cmbInventory,
                    nudChocolateBar,
                    nudHealthPills,
                    chkMV9, chkVPacker,
                    nudMV9Ammo, nudVPackerAmmo,
                    chkBoranX, nudBoranXAmmo,
                    nudSmallMedipack,
                    nudHealthBandages,
                    chkK2Impactor, nudK2ImpactorAmmo,
                    nudLargeHealthPack,
                    chkScorpionX, nudScorpionXAmmo,
                    chkVectorR35, nudVectorR35Ammo,
                    chkDesertRanger, nudDesertRangerAmmo,
                    chkDartSS, nudDartSSAmmo,
                    chkRigg09, nudRigg09Ammo,
                    chkViperSMG, nudViperSMGAmmo,
                    chkMagVega, nudMagVegaAmmo,
                    chkVectorR35Pair, chkScorpionXPair,
                    nudPoisonAntidote
                );

                EnableButtons();
            }
        }

        private void nudViperSMGAmmo_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (!isLoading && !isInventoryLoading && cmbInventory.SelectedIndex != -1)
            {
                UpdateInventoryFromUI(
                    cmbInventory,
                    nudChocolateBar,
                    nudHealthPills,
                    chkMV9, chkVPacker,
                    nudMV9Ammo, nudVPackerAmmo,
                    chkBoranX, nudBoranXAmmo,
                    nudSmallMedipack,
                    nudHealthBandages,
                    chkK2Impactor, nudK2ImpactorAmmo,
                    nudLargeHealthPack,
                    chkScorpionX, nudScorpionXAmmo,
                    chkVectorR35, nudVectorR35Ammo,
                    chkDesertRanger, nudDesertRangerAmmo,
                    chkDartSS, nudDartSSAmmo,
                    chkRigg09, nudRigg09Ammo,
                    chkViperSMG, nudViperSMGAmmo,
                    chkMagVega, nudMagVegaAmmo,
                    chkVectorR35Pair, chkScorpionXPair,
                    nudPoisonAntidote
                );

                EnableButtons();
            }
        }

        private void nudScorpionXAmmo_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (!isLoading && !isInventoryLoading && cmbInventory.SelectedIndex != -1)
            {
                UpdateInventoryFromUI(
                    cmbInventory,
                    nudChocolateBar,
                    nudHealthPills,
                    chkMV9, chkVPacker,
                    nudMV9Ammo, nudVPackerAmmo,
                    chkBoranX, nudBoranXAmmo,
                    nudSmallMedipack,
                    nudHealthBandages,
                    chkK2Impactor, nudK2ImpactorAmmo,
                    nudLargeHealthPack,
                    chkScorpionX, nudScorpionXAmmo,
                    chkVectorR35, nudVectorR35Ammo,
                    chkDesertRanger, nudDesertRangerAmmo,
                    chkDartSS, nudDartSSAmmo,
                    chkRigg09, nudRigg09Ammo,
                    chkViperSMG, nudViperSMGAmmo,
                    chkMagVega, nudMagVegaAmmo,
                    chkVectorR35Pair, chkScorpionXPair,
                    nudPoisonAntidote
                );

                EnableButtons();
            }
        }

        private void nudMagVegaAmmo_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (!isLoading && !isInventoryLoading && cmbInventory.SelectedIndex != -1)
            {
                UpdateInventoryFromUI(
                    cmbInventory,
                    nudChocolateBar,
                    nudHealthPills,
                    chkMV9, chkVPacker,
                    nudMV9Ammo, nudVPackerAmmo,
                    chkBoranX, nudBoranXAmmo,
                    nudSmallMedipack,
                    nudHealthBandages,
                    chkK2Impactor, nudK2ImpactorAmmo,
                    nudLargeHealthPack,
                    chkScorpionX, nudScorpionXAmmo,
                    chkVectorR35, nudVectorR35Ammo,
                    chkDesertRanger, nudDesertRangerAmmo,
                    chkDartSS, nudDartSSAmmo,
                    chkRigg09, nudRigg09Ammo,
                    chkViperSMG, nudViperSMGAmmo,
                    chkMagVega, nudMagVegaAmmo,
                    chkVectorR35Pair, chkScorpionXPair,
                    nudPoisonAntidote
                );

                EnableButtons();
            }
        }

        private void nudBoranXAmmo_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (!isLoading && !isInventoryLoading && cmbInventory.SelectedIndex != -1)
            {
                UpdateInventoryFromUI(
                    cmbInventory,
                    nudChocolateBar,
                    nudHealthPills,
                    chkMV9, chkVPacker,
                    nudMV9Ammo, nudVPackerAmmo,
                    chkBoranX, nudBoranXAmmo,
                    nudSmallMedipack,
                    nudHealthBandages,
                    chkK2Impactor, nudK2ImpactorAmmo,
                    nudLargeHealthPack,
                    chkScorpionX, nudScorpionXAmmo,
                    chkVectorR35, nudVectorR35Ammo,
                    chkDesertRanger, nudDesertRangerAmmo,
                    chkDartSS, nudDartSSAmmo,
                    chkRigg09, nudRigg09Ammo,
                    chkViperSMG, nudViperSMGAmmo,
                    chkMagVega, nudMagVegaAmmo,
                    chkVectorR35Pair, chkScorpionXPair,
                    nudPoisonAntidote
                );

                EnableButtons();
            }
        }

        private void nudPoisonAntidote_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (!isLoading && !isInventoryLoading && cmbInventory.SelectedIndex != -1)
            {
                UpdateInventoryFromUI(
                    cmbInventory,
                    nudChocolateBar,
                    nudHealthPills,
                    chkMV9, chkVPacker,
                    nudMV9Ammo, nudVPackerAmmo,
                    chkBoranX, nudBoranXAmmo,
                    nudSmallMedipack,
                    nudHealthBandages,
                    chkK2Impactor, nudK2ImpactorAmmo,
                    nudLargeHealthPack,
                    chkScorpionX, nudScorpionXAmmo,
                    chkVectorR35, nudVectorR35Ammo,
                    chkDesertRanger, nudDesertRangerAmmo,
                    chkDartSS, nudDartSSAmmo,
                    chkRigg09, nudRigg09Ammo,
                    chkViperSMG, nudViperSMGAmmo,
                    chkMagVega, nudMagVegaAmmo,
                    chkVectorR35Pair, chkScorpionXPair,
                    nudPoisonAntidote
                );

                EnableButtons();
            }
        }
    }
}