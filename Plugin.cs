using Rocket.API.Collections;
using Rocket.Core.Plugins;
using Rocket.Core.Utils;
using Rocket.Unturned.Chat;
using Rocket.Unturned.Events;
using Rocket.Unturned.Player;
using SDG.Unturned;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Steamworks;
using FinxRecycler.Types;


// For anyone who is snooping around in here i will leave comments on what everything does and if you have any questions, add me on discord "finx1"
// but please dont copy my work, thank you! and if you have any questions about logic or improvments i can make just contact me
namespace FinxRecycler
{
    public class Plugin : RocketPlugin<Config>
    {
        public override TranslationList DefaultTranslations => new TranslationList
        {
            { "recycler_start", "Recycler has started!" }, //translations (pretty self explanatory
            { "recycler_end", "Recycling finished!" } 
        };

        public static Plugin Instance { get; private set; }
        

        protected override void Load() //loading the onplayerupdategesture event
        {
            Instance = this;
            UnturnedPlayerEvents.OnPlayerUpdateGesture += UnturnedPlayerEvents_OnPlayerUpdateGesture;
        }

       

        private void UnturnedPlayerEvents_OnPlayerUpdateGesture(UnturnedPlayer player, UnturnedPlayerEvents.PlayerGesture gesture) // parameters for the logic
        {
            try
            {
                if (gesture != UnturnedPlayerEvents.PlayerGesture.PunchLeft && gesture != UnturnedPlayerEvents.PlayerGesture.PunchRight && gesture != UnturnedPlayerEvents.PlayerGesture.Point)
                {
                    
                    return; //if the gesture is NOT "Punch" or "Point" then exit the code
                }

                var storage = player.GetRaycastStorage(out ushort storageId); //go to the "Main.cs" class to look at the getraycaststorage logic

                if (storage == null) //if the storage is not a "recycler" or is not a "storage" then it returns as null hence exiting the code
                {
                    
                    return; 
                }

                var recycler = Configuration.Instance.Recyclers.Find(x => x.StorageId == storageId); // finds the id of the storage/recycler in the config

                if (recycler == null)
                {
                   
                    return;
                }

                

                
                if (!HasRecyclableItems(storage, recycler)) // Check if there are any recyclable items in the storage
                {
                   
                    if (Configuration.Instance.EnableDebugLogs) // debug logs outputting to console for testing (mainly used for finx only but could be used if some severs have issues with the plugin)
                    {
                        Debug.Log("No recyclable items found in the storage. Aborting recycling process.");
                    }
                    return;
                }



                string colorCode = Configuration.Instance.ChatColor; // color for chat message
                Color chatColor;

                if (ColorUtility.TryParseHtmlString(colorCode, out chatColor)) // converting hex colors into colors that unturned understands
                {
                    string iconUrl = Configuration.Instance.ChatIconUrl; //setting the iconurl for the message
                    ChatManager.serverSendMessage(Translate("recycler_start"), chatColor, null, player.SteamPlayer(), EChatMode.SAY, iconUrl, false); // Sending start message
                } //this is the only comment i will write about chatmessages since no need for anymore
                else
                {
                    Debug.LogWarning($"Invalid color code: {colorCode}"); // if the color code is invalid it will output a debug log
                }
                StartCoroutine(RecycleItems(player, storage, recycler)); // starts the coroutine of "recycle items"

            }
            catch (Exception ex)
            {
                Debug.LogError($"Exception in OnPlayerUpdateGesture: {ex}");
                Rocket.Core.Logging.Logger.LogException(ex, "Exception in OnPlayerUpdateGesture()");
            }
        }

        private bool HasRecyclableItems(InteractableStorage storage, Recycler recycler) // logic for checking if the storage has "recycleable items" in it
        {
            
            if (Configuration.Instance.EnableDebugLogs)
            {
                Debug.Log("Checking items in storage:");
            }
            foreach (var item in storage.items.items)
            {
                
                if (Configuration.Instance.EnableDebugLogs)
                {
                    Debug.Log($"Item ID: {item.item.id}, Amount: {item.item.amount}");
                }

                ushort inputId;
                if (recycler.GetRecipe(item.item.id, out inputId) != null)
                {
                    // At least one item in the storage can be recycled
                   
                    if (Configuration.Instance.EnableDebugLogs)
                    {
                        Debug.Log($"Recyclable item found - Item ID: {item.item.id}, Amount: {item.item.amount}");
                    }

                    return true; // Return true as soon as a recyclable item is found
                }
            }


            if (Configuration.Instance.EnableDebugLogs)
            {
                Debug.Log($"No Recycleable items found in storage");
            }

            return false; // Return false if no recyclable items were found in the entire loop
        }

        

        

        private System.Collections.IEnumerator RecycleItems(UnturnedPlayer player, InteractableStorage storage, Recycler recycler)
        {
            float delay = recycler.Delay; // delays the logic based on the config option

            if (delay > 0)
            {
                if (Configuration.Instance.EnableDebugLogs)
                {
                    Debug.Log($"Delaying recycling for {delay} seconds.");
                }

                yield return new WaitForSeconds(delay);
            }

            try
            {
                RecycleNextItem(player, storage, recycler);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Exception in RecycleItems: {ex}");
                Rocket.Core.Logging.Logger.LogException(ex, "Exception in RecycleItems()");
            }
            finally
            {
                
                if (Configuration.Instance.EnableDebugLogs)
                {
                    Debug.Log("Exiting RecycleItems coroutine.");
                }
            }
        }

        private void RecycleNextItem(UnturnedPlayer player, InteractableStorage storage, Recycler recycler) //logic for recycling all items in the storage
        {
            foreach (var storageItem in storage.items.items)
            {
                var itemToRecycle = storageItem.item;

                ushort inputId;
                var recipe = recycler.GetRecipe(itemToRecycle.id, out inputId); 

                if (recipe != null)
                {
                    int inputAmount = itemToRecycle.amount;

                    ItemAsset outputItemAsset = Assets.find(EAssetType.ITEM, recipe.OutputId) as ItemAsset; // STACKING SUPPORT - finds if the item already has an "amount" specified in the asset, go to "stacksupport.cs" to view the code for checking that 

                    if (outputItemAsset != null && outputItemAsset.amount > 1) // if the amount in the asset is not 0 or 1 then it continues with the code for adding items that can stack
                    {
                       
                        if (Configuration.Instance.EnableDebugLogs)
                        {
                            Debug.Log($"Output item asset ID {recipe.OutputId} already has an amount specified: {outputItemAsset.amount}");
                        }
                        int adjustedOutputAmount = recipe.OutputAmount * inputAmount; // STACKING SUPPORT - multiplies the input amount by the stack amount to calculate how many "stackable" items to add so that there are no errors

                        while (adjustedOutputAmount > 0)
                        {
                            int stackSize = Math.Min(adjustedOutputAmount, Configuration.Instance.MaxStackAmount);
                            Item newItem = new Item(recipe.OutputId, (byte)stackSize, 0); // creates new "item" to add with metadata

                            if (storage.items.tryAddItem(newItem, true)) // using "tryadditem" instead of "additem" since add item doesnt check for available slots and just adds it to the first slot which can cause alot of issues
                            {

                                if (Configuration.Instance.EnableDebugLogs)
                                {
                                    Debug.Log($"Added {stackSize} items of ID {recipe.OutputId} to storage.");
                                }
                                adjustedOutputAmount -= stackSize; // config option maxstacksize will determine your maximum amount of items in 1 stack based off your asset file so if the output totals more than your "max stack amount" it will create a new stack with the leftovers of the amount of items to add
                            }
                            else
                            {
                                
                                if (Configuration.Instance.EnableDebugLogs)
                                {
                                    Debug.LogWarning("Not enough space in storage. Exiting the coroutine.");
                                }
                                return;
                            }
                        }
                    }
                    else
                    {
                        // Use the second logic to add items if the asset does not have a specified "amount" in the file 
                        int outputAmount = recipe.OutputAmount <= 0 ? 1 : recipe.OutputAmount;
                        Item newItem = new Item(recipe.OutputId, (byte)outputAmount, 100); // the outputamount now just outputs lets say 9 soda cans instead of 9 soda cans stacked togethor

                        while (outputAmount > 0)
                        {
                            if (storage.items.tryAddItem(newItem, true)) //using "tryadditem" again
                            {
                                
                                if (Configuration.Instance.EnableDebugLogs)
                                {
                                    Debug.Log($"Added {outputAmount} items of ID {recipe.OutputId} to storage.");
                                }
                                outputAmount--;
                            }
                            else
                            {
                                
                                if (Configuration.Instance.EnableDebugLogs)
                                {
                                    Debug.LogWarning("Not enough space in storage. Exiting the coroutine.");
                                }
                                return;
                            }
                        }
                    }

                    // Remove the item from storage
                    byte byteIndexToRemove = (byte)storage.items.items.IndexOf(storageItem);
                    storage.items.removeItem(byteIndexToRemove);

                    // Continue to recycle the next item
                    RecycleNextItem(player, storage, recycler); //this will keep on repeating till there are no more items left in the recycler to recycle
                    return; // Exit the current function call 
                }
            }

            // All items have been recycled at this point
            
            if (Configuration.Instance.EnableDebugLogs)
            {
                Debug.Log("Recycling finished!");
            }

            
           
            string colorCode = Configuration.Instance.ChatColor;
            Color chatColor;

            if (ColorUtility.TryParseHtmlString(colorCode, out chatColor))
            {
                string iconUrl = Configuration.Instance.ChatIconUrl;
                ChatManager.serverSendMessage(Translate("recycler_end"), chatColor, null, player.SteamPlayer(), EChatMode.SAY, iconUrl, false); // Send ending message
            }
            else
            {
                Debug.LogWarning($"Invalid color code: {colorCode}");
            }


            ushort effectId = recycler.EffectId;
            if (effectId != 0)
            {
                if (Configuration.Instance.EnableDebugLogs)
                {
                    Debug.Log($"Triggering effect with ID: {effectId} at position: {storage.transform.position} for player: {player.CharacterName}");
                }
               
                TriggerEffect(effectId, storage.transform.position, player.CSteamID); // trigger the effect (we dont have this in the loop since it would repeat the effect multiple times)
            }

            
        }


    
            




        public void TriggerEffect(ushort effectId, Vector3 position, CSteamID relevantPlayerID) // trigger effect logic
        {
            TriggerEffectParameters parameters = new TriggerEffectParameters(effectId);
            parameters.position = position;
            parameters.relevantPlayerID = relevantPlayerID;
            EffectManager.triggerEffect(parameters);
        }

        protected override void Unload() // unloading the event
        {
            UnturnedPlayerEvents.OnPlayerUpdateGesture -= UnturnedPlayerEvents_OnPlayerUpdateGesture;
        }
    }
}
