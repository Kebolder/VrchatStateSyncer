using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using VRC.SDK3.Avatars.Components;
using static VRC.SDKBase.VRC_AvatarParameterDriver;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using System.Linq;

namespace JaxTools.AnimatorTools
{
    /// Utilities to add and verify VRChat Avatar Parameter Drivers on animator states.
    /// Supports simple int-based drivers and binary (multi-bool) driver sets.
    public static class ParameterDriverUtilities
    {
        private static System.Type[] cachedVRChatTypes;
        private static bool typesResolved = false;

        /// Adds a VRChat Avatar Parameter Driver to the given state that sets the specified
        /// int parameter to the provided state number if such a driver/entry does not already exist.
        /// <param name="state">Animator state to attach the driver to.</param>
        /// <param name="stateNumber">Desired int value to set.</param>
        /// <param name="parameterName">Animator parameter name to modify.</param>
        public static void AddParameterDriverToState(AnimatorState state, int stateNumber, string parameterName)
        {
            try
            {
                if (!ValidateParameterDriverInput(state, stateNumber, parameterName, out string errorMessage))
                {
                    return;
                }

                if (HasExistingParameterDriver(state, stateNumber, parameterName))
                {
                    Debug.Log($"Parameter Driver with parameter '{parameterName}' = {stateNumber} already exists on state: {state.name}");
                    return;
                }

                if (!TryResolveVRChatTypes(out System.Type parameterDriverType, out System.Type parameterEntryType, out System.Type changeTypeEnum, out errorMessage))
                {
                    return;
                }

                StateMachineBehaviour parameterDriver = state.AddStateMachineBehaviour(parameterDriverType);
                
                ConfigureParameterDriverProperties(parameterDriver, parameterDriverType);
                
                if (!TryAddParameterEntry(parameterDriver, parameterDriverType, parameterEntryType, changeTypeEnum,
                                         parameterName, stateNumber, out errorMessage))
                {
                    return;
                }

                Debug.Log($"Successfully added Parameter Driver to state: {state.name} with parameter: {parameterName} = {stateNumber}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error adding Parameter Driver to state {state?.name}: {e.Message}");
            }
        }

        /// Checks whether a matching Parameter Driver entry already exists on the state
        /// to prevent duplicate entries for the same target value.
        private static bool HasExistingParameterDriver(AnimatorState state, int stateNumber, string parameterName)
        {
            if (state?.behaviours == null) return false;

            System.Type parameterDriverType = typeof(VRCAvatarParameterDriver);
            
            if (state.behaviours.Length == 0) return false;
            
            foreach (StateMachineBehaviour behaviour in state.behaviours)
            {
                if (behaviour != null && behaviour.GetType() == parameterDriverType)
                {
                    try
                    {
                        using (SerializedObject serializedDriver = new SerializedObject(behaviour))
                        {
                            SerializedProperty parametersArrayProp = serializedDriver.FindProperty("parameters");
                            
                            if (parametersArrayProp != null && parametersArrayProp.isArray)
                            {
                                if (parametersArrayProp.arraySize == 0) continue;
                                
                                for (int i = 0; i < parametersArrayProp.arraySize; i++)
                                {
                                    SerializedProperty parameterProp = parametersArrayProp.GetArrayElementAtIndex(i);
                                    SerializedProperty nameProp = parameterProp.FindPropertyRelative("name");
                                    SerializedProperty typeProp = parameterProp.FindPropertyRelative("type");
                                    SerializedProperty valueProp = parameterProp.FindPropertyRelative("value");
                                    
                                    if (nameProp != null && typeProp != null && valueProp != null &&
                                        nameProp.stringValue == parameterName &&
                                        typeProp.enumValueIndex == (int)ChangeType.Set &&
                                        Math.Abs(valueProp.floatValue - (float)stateNumber) < 0.001f)
                                    {
                                        return true;
                                    }
                                }
                            }
                        }
                    }
                    catch (System.Exception)
                    {
                        continue;
                    }
                }
            }
            
            return false;
        }

        /// Validates inputs for adding a single parameter driver entry.
        private static bool ValidateParameterDriverInput(AnimatorState state, int stateNumber, string parameterName, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (state == null)
            {
                errorMessage = "Animator state cannot be null";
                return false;
            }

            if (string.IsNullOrEmpty(parameterName))
            {
                errorMessage = "Parameter name cannot be null or empty";
                return false;
            }

            if (stateNumber < 0)
            {
                errorMessage = "State number cannot be negative";
                return false;
            }

            return true;
        }
        
        /// Resolves VRChat SDK types required for interacting with Avatar Parameter Driver via reflection.
        private static bool TryResolveVRChatTypes(out System.Type parameterDriverType, out System.Type parameterEntryType,
                                           out System.Type changeTypeEnum, out string errorMessage)
        {
            parameterDriverType = null;
            parameterEntryType = null;
            changeTypeEnum = null;
            errorMessage = string.Empty;
            
            if (!typesResolved)
            {
                cachedVRChatTypes = ResolveVRChatSDKTypes();
                typesResolved = true;
            }
            
            if (cachedVRChatTypes[0] == null)
            {
                errorMessage = "VRChat AvatarParameterDriver type not found. Make sure VRChat SDK is installed.";
                return false;
            }

            if (cachedVRChatTypes[1] == null)
            {
                errorMessage = "VRChat ParameterEntry type not found.";
                return false;
            }

            if (cachedVRChatTypes[2] == null)
            {
                errorMessage = "VRChat ChangeType enum not found.";
                return false;
            }

            parameterDriverType = cachedVRChatTypes[0];
            parameterEntryType = cachedVRChatTypes[1];
            changeTypeEnum = cachedVRChatTypes[2];
            
            return true;
        }
        
        /// Returns the concrete VRChat SDK types used by this utility. Split for caching.
        private static System.Type[] ResolveVRChatSDKTypes()
        {
            return new System.Type[]
            {
                typeof(VRCAvatarParameterDriver),
                typeof(Parameter),
                typeof(ChangeType)
            };
        }
        
        /// Sets initial driver properties (e.g., disables Local Only if available) for consistent behavior.
        private static void ConfigureParameterDriverProperties(StateMachineBehaviour parameterDriver, System.Type parameterDriverType)
        {
            var localOnlyField = parameterDriverType.GetField("localOnly", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (localOnlyField != null)
            {
                try
                {
                    localOnlyField.SetValue(parameterDriver, false);
                }
                catch (System.Exception)
                {
                }
            }
        }
        
        /// Appends a new parameter entry to the driver via SerializedObject, setting it to the desired value.
        private static bool TryAddParameterEntry(StateMachineBehaviour parameterDriver, System.Type parameterDriverType,
                                          System.Type parameterEntryType, System.Type changeTypeEnum,
                                          string parameterName, int stateNumber, out string errorMessage)
        {
            errorMessage = string.Empty;

            try
            {
                Parameter newParameter = new Parameter
                {
                    name = parameterName,
                    type = ChangeType.Set,
                    value = (float)stateNumber
                };
                
                using (SerializedObject serializedDriver = new SerializedObject(parameterDriver))
                {
                    SerializedProperty parametersArrayProp = serializedDriver.FindProperty("parameters");
                    
                    if (parametersArrayProp == null)
                    {
                        errorMessage = "Parameters array property not found on Parameter Driver";
                        return false;
                    }
                    
                    int newIndex = parametersArrayProp.arraySize;
                    parametersArrayProp.arraySize++;
                    
                    SerializedProperty newElement = parametersArrayProp.GetArrayElementAtIndex(newIndex);
                    
                    SerializedProperty nameProperty = newElement.FindPropertyRelative("name");
                    SerializedProperty typeProperty = newElement.FindPropertyRelative("type");
                    SerializedProperty valueProperty = newElement.FindPropertyRelative("value");
                    
                    if (nameProperty != null) nameProperty.stringValue = newParameter.name;
                    if (typeProperty != null) typeProperty.enumValueIndex = (int)newParameter.type;
                    if (valueProperty != null) valueProperty.floatValue = newParameter.value;
                    
                    serializedDriver.ApplyModifiedProperties();
                }
                
                return true;
            }
            catch (System.Exception e)
            {
                errorMessage = $"Exception while adding parameter entry: {e.Message}";
                Debug.LogError($"Error adding parameter entry: {errorMessage}");
                return false;
            }
        }
        /// Adds a set of Parameter Driver entries that encode the state number into a binary pattern
        /// across multiple boolean parameters.
        public static void AddBinaryParameterDriversToState(AnimatorState state, int stateNumber, string[] parameterNames)
        {
            try
            {
                if (!ValidateBinaryParameterDriverInput(state, stateNumber, parameterNames, out string errorMessage))
                {
                    Debug.LogError($"Binary parameter driver validation failed: {errorMessage}");
                    return;
                }

                if (HasExistingBinaryParameterDrivers(state, stateNumber, parameterNames))
                {
                    Debug.Log($"Binary Parameter Drivers with state {stateNumber} already exist on state: {state.name}");
                    return;
                }

                if (!TryResolveVRChatTypes(out System.Type parameterDriverType, out System.Type parameterEntryType, out System.Type changeTypeEnum, out errorMessage))
                {
                    Debug.LogError($"Failed to resolve VRChat types: {errorMessage}");
                    return;
                }

                var binaryConditions = BinaryEncoder.GenerateParameterConditions(stateNumber, parameterNames);
                
                bool[] binary = BinaryEncoder.StateNumberToBinary(stateNumber, parameterNames.Length);
                string binaryStr = string.Join("", binary.Select(b => b ? "1" : "0"));
                Debug.Log($"Adding parameter drivers for state {state.name} (Number: {stateNumber}) Binary: {binaryStr}");

                StateMachineBehaviour parameterDriver = state.AddStateMachineBehaviour(parameterDriverType);
                ConfigureParameterDriverProperties(parameterDriver, parameterDriverType);

                foreach (var condition in binaryConditions)
                {
                    bool value = condition.Value;
                    float floatValue = value ? 1f : 0f;
                    
                    Debug.Log($"  Adding parameter driver: {condition.ParameterName} = {(value ? "true" : "false")} ({floatValue})");
                    
                    if (!TryAddParameterEntry(parameterDriver, parameterDriverType, parameterEntryType, changeTypeEnum,
                                             condition.ParameterName, (int)floatValue, out errorMessage))
                    {
                        Debug.LogWarning($"Failed to add parameter entry for {condition.ParameterName}: {errorMessage}");
                    }
                }

                Debug.Log($"Successfully added Binary Parameter Drivers to state: {state.name} for state number: {stateNumber}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error adding Binary Parameter Drivers to state {state?.name}: {e.Message}");
            }
        }

        /// Verifies if the state already has a binary-encoded set of driver entries matching the target state number.
        public static bool HasExistingBinaryParameterDrivers(AnimatorState state, int stateNumber, string[] parameterNames)
        {
            if (state?.behaviours == null || parameterNames == null || parameterNames.Length == 0)
            {
                return false;
            }

            System.Type parameterDriverType = typeof(VRCAvatarParameterDriver);
            
            if (state.behaviours.Length == 0) return false;
            
            foreach (StateMachineBehaviour behaviour in state.behaviours)
            {
                if (behaviour != null && behaviour.GetType() == parameterDriverType)
                {
                    try
                    {
                        using (SerializedObject serializedDriver = new SerializedObject(behaviour))
                        {
                            SerializedProperty parametersArrayProp = serializedDriver.FindProperty("parameters");
                            
                            if (parametersArrayProp != null && parametersArrayProp.isArray)
                            {
                                if (parametersArrayProp.arraySize == 0) continue;
                                
                                var parameterConditions = new List<string>();
                                
                                for (int i = 0; i < parametersArrayProp.arraySize; i++)
                                {
                                    SerializedProperty parameterProp = parametersArrayProp.GetArrayElementAtIndex(i);
                                    SerializedProperty nameProp = parameterProp.FindPropertyRelative("name");
                                    SerializedProperty typeProp = parameterProp.FindPropertyRelative("type");
                                    SerializedProperty valueProp = parameterProp.FindPropertyRelative("value");
                                    
                                    if (nameProp != null && typeProp != null && valueProp != null &&
                                        typeProp.enumValueIndex == (int)ChangeType.Set)
                                    {
                                        parameterConditions.Add($"{nameProp.stringValue}={valueProp.floatValue}");
                                    }
                                }
                                
                                var expectedConditions = BinaryEncoder.GenerateParameterConditions(stateNumber, parameterNames);
                                var expectedConditionsString = expectedConditions
                                    .OrderBy(c => c.ParameterName)
                                    .Select(c => $"{c.ParameterName}={(c.Value ? 1f : 0f)}")
                                    .ToList();
                                
                                var actualConditionsString = parameterConditions.OrderBy(c => c).ToList();
                                
                                if (actualConditionsString.SequenceEqual(expectedConditionsString))
                                {
                                    return true;
                                }
                            }
                        }
                    }
                    catch (System.Exception)
                    {
                        continue;
                    }
                }
            }
            
            return false;
        }
        /// Convenience wrapper to create animator transition conditions that match a binary-encoded state.
        public static AnimatorCondition[] CreateBinaryConditions(int targetState, string[] parameterNames)
        {
            return BinaryEncoder.CreateBinaryConditions(targetState, parameterNames);
        }

        /// Validates inputs for adding binary parameter drivers.
        private static bool ValidateBinaryParameterDriverInput(AnimatorState state, int stateNumber, string[] parameterNames, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (state == null)
            {
                errorMessage = "Animator state cannot be null";
                return false;
            }

            if (parameterNames == null || parameterNames.Length == 0)
            {
                errorMessage = "Parameter names array cannot be null or empty";
                return false;
            }

            if (stateNumber < 0)
            {
                errorMessage = "State number cannot be negative";
                return false;
            }

            var duplicates = parameterNames.ToList().GroupBy(p => p).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (duplicates.Count > 0)
            {
                errorMessage = "Duplicate parameter names found: " + string.Join(", ", duplicates);
                return false;
            }

            return true;
        }
    }
}