using System;
using System.Collections.Generic;
using System.Json;
using System.Linq;
using Newtonsoft.Json;
using Totem.Infrastructure;
using Totem.Models;

namespace Totem.Services
{
    public class TesterService
    {
        private static readonly List<TestMessageResult> TestCases = new List<TestMessageResult>();
        private static bool _isContractValid = true;
        private static string _contractErrorMessage = "";

        public TestMessageResult Execute(string contract, string message)
        {
            var schemaDictionary = SchemaObject.BuildSchemaDictionary(contract, HandleReferenceError, HandleFailure);

            if (!_isContractValid)
            {
                var contractInvalidMessage = new TestMessageResult
                {
                    IsMessageValid = false,
                    MessageErrors = new List<string>() { _contractErrorMessage }
                };
                _isContractValid = true;
                _contractErrorMessage = "";
                return contractInvalidMessage;
            }

            if (!TryParseJSON(message, out var messageJson))
            {
                return new TestMessageResult
                {
                    IsMessageValid = false,
                    MessageErrors = new List<string> { "Message contains invalid JSON." }
                };
            }

            var messageDictionary = JsonConvert.DeserializeObject<CaseInsensitiveDictionary<object>>(messageJson.ToString());

            if (messageDictionary.Count <= 0)
                return new TestMessageResult
                {
                    IsMessageValid = false,
                    MessageErrors = new List<string> { "Message contains empty JSON." }
                };

            var contractDictionary = schemaDictionary.TryGetValue("Contract", out _)
                ? schemaDictionary["Contract"].Properties
                : null;

            if (contractDictionary != null && _isContractValid)
            {
                TestCases.Clear();
                TestCases.Add(AreAllElementsInMessageContainedInContract(messageDictionary, contractDictionary));
                TestCases.Add(AreAllElementsInContractContainedInMessage(messageDictionary, contractDictionary));
                TestCases.Add(DoAllMessageValuesMatchDataTypes(messageDictionary, contractDictionary));
            }

            return new TestMessageResult
            {
                IsMessageValid = TestCases.All(x => x.IsMessageValid),
                MessageErrors = TestCases.SelectMany(x => x.MessageErrors).ToList()
            };
        }

        private static TestMessageResult HandleReferenceError()
        {
            _isContractValid = false;
            _contractErrorMessage = "Unable to test; reference definition is invalid.";

            return new TestMessageResult()
            {
                IsMessageValid = false,
                MessageErrors = new List<string>() { _contractErrorMessage }
            };
        }

        private static TestMessageResult HandleFailure()
        {
            _isContractValid = false;
            _contractErrorMessage = "Unable to test; contract is not a valid OpenAPI schema.";

            return new TestMessageResult()
            {
                IsMessageValid = false,
                MessageErrors = new List<string>() { _contractErrorMessage }
            };
        }

        public TestMessageResult AreAllElementsInMessageContainedInContract(CaseInsensitiveDictionary<object> messageKeyDictionary, CaseInsensitiveDictionary<SchemaObject> contractDictionary)
        {
            var result = new TestMessageResult();

            foreach (var kv in messageKeyDictionary)
            {
                if (contractDictionary.Keys.Contains(kv.Key, StringComparer.InvariantCultureIgnoreCase)) continue;

                result.IsMessageValid = false;
                result.MessageErrors.Add($"Message property \"{kv.Key}\" is not part of the contract.");
            }

            return result;
        }

        public TestMessageResult AreAllElementsInContractContainedInMessage(CaseInsensitiveDictionary<object> messageKeyDictionary, CaseInsensitiveDictionary<SchemaObject> contractDictionary)
        {
            var result = new TestMessageResult();

            foreach (var kv in contractDictionary)
            {
                if (messageKeyDictionary.Keys.Contains(kv.Key, StringComparer.InvariantCultureIgnoreCase)) continue;

                result.IsMessageValid = false;
                result.MessageErrors.Add($"Message is missing expected property \"{kv.Key}\".");
            }

            return result;
        }

        public TestMessageResult DoAllMessageValuesMatchDataTypes(CaseInsensitiveDictionary<object> messageKeyDictionary, CaseInsensitiveDictionary<SchemaObject> contractDictionary)
        {
            var testMessageResult = new TestMessageResult();

            // loop through each property in the message Dictionary - trying to parse the values into the data type in the contract
            foreach (KeyValuePair<string, object> kv in messageKeyDictionary)
            {
                // if the contract doesn't supply the datatype for the parameter -> then the message doesn't match the contract.
                contractDictionary.TryGetValue(kv.Key, out var propertySchemaObject);

                if (propertySchemaObject != null)
                {
                    ChecksForMessage(propertySchemaObject, kv, testMessageResult);
                }
                else
                {
                    AddSchemaNotFoundError(testMessageResult, kv.Key);
                }
            }

            return testMessageResult;
        }

        private void ChecksForMessage(SchemaObject propertySchemaObject, KeyValuePair<string, object> kv,
            TestMessageResult testMessageResult)
        {
            var dataType = propertySchemaObject.GetDataType();

            if (dataType == DataType.Integer)
            {
                CheckIntegerType(propertySchemaObject, kv, testMessageResult);
            }

            if (dataType == DataType.Number)
            {
                CheckNumberType(propertySchemaObject, kv, testMessageResult);
            }

            if (dataType == DataType.String)
            {
                CheckStringType(propertySchemaObject, kv, testMessageResult);
            }

            if (dataType == DataType.Array)
            {
                CheckArrayType(propertySchemaObject, kv, testMessageResult);
            }

            if (dataType == DataType.Object)
            {
                CheckObjectType(propertySchemaObject, kv, testMessageResult);
            }
        }

        private static void CheckIntegerType(SchemaObject propertySchemaObject, KeyValuePair<string, object> kv,
            TestMessageResult testMessageResult)
        {
            var isInt32 = int.TryParse(kv.Value.ToString(), out _);
            var isInt64 = long.TryParse(kv.Value.ToString(), out _);

            // Validate integer data type
            if (!isInt32 && !isInt64)
            {
                AddTypeError(testMessageResult, kv.Value.ToString(), kv.Key, propertySchemaObject.Type);
            }

            if (propertySchemaObject.Format != null)
            {
                // Validate specific integer formats
                if (propertySchemaObject.Format.EqualsCaseInsensitive("int32") && !isInt32)
                {
                    AddFormatError(testMessageResult, kv.Value.ToString(), kv.Key, Format.Int32.DisplayName);
                }
                else if (propertySchemaObject.Format.EqualsCaseInsensitive("int64") && !isInt64)
                {
                    AddFormatError(testMessageResult, kv.Value.ToString(), kv.Key, Format.Int64.DisplayName);
                }
            }
        }

        private static void CheckNumberType(SchemaObject propertySchemaObject, KeyValuePair<string, object> kv,
            TestMessageResult testMessageResult)
        {
            var isDouble = double.TryParse(kv.Value.ToString(), out _);
            var isFloat = float.TryParse(kv.Value.ToString(), out _);
            
            // Validate number data type
            if (!isDouble && !isFloat)
            {
                AddTypeError(testMessageResult, kv.Value.ToString(), kv.Key, propertySchemaObject.Type);
            }

            if (propertySchemaObject.Format != null)
            {
                // Validate specific number formats
                if (propertySchemaObject.Format.EqualsCaseInsensitive("float") && !isFloat)
                {
                    AddFormatError(testMessageResult, kv.Value.ToString(), kv.Key, "Float");
                }
                else if (propertySchemaObject.Format.EqualsCaseInsensitive("double") && !isDouble)
                {
                    AddFormatError(testMessageResult, kv.Value.ToString(), kv.Key, "Double");
                }
            }
        }

        private static void CheckStringType(SchemaObject propertySchemaObject, KeyValuePair<string, object> kv,
            TestMessageResult testMessageResult)
        {
            if (kv.Value != null)
            {
                var notGuid = (!Guid.TryParse(kv.Value.ToString(), out _));
                var notDateTime = (!DateTime.TryParse(kv.Value.ToString(), out _));

                if (propertySchemaObject.Format != null)
                {
                    // Validate specific string formats
                    if (propertySchemaObject.Format.EqualsCaseInsensitive("date-time") && notDateTime)
                    {
                        AddFormatError(testMessageResult, kv.Value.ToString(), kv.Key, Format.DateTime.DisplayName);
                    }
                }
                if (notGuid && propertySchemaObject.Reference == "Guid")
                {
                    AddFormatError(testMessageResult, kv.Value.ToString(), kv.Key, Format.Guid.DisplayName);
                }
            }
            else if (kv.Value == null && propertySchemaObject.Reference == "Guid")
            {
                AddFormatError(testMessageResult, null, kv.Key, Format.Guid.DisplayName);
            }
        }

        private void CheckArrayType(SchemaObject propertySchemaObject, KeyValuePair<string, object> kv,
            TestMessageResult testMessageResult)
        {
            if (propertySchemaObject.Items == null)
            {
                AddRequiredError(testMessageResult, kv.Key, "Array", "Items");
            }
            else
            {
                var itemSchema = propertySchemaObject.Items;
                dynamic itemArray = JsonConvert.DeserializeObject(kv.Value.ToString());
                var count = 0;
                foreach (var _ in itemArray)
                {
                    count += 1;
                }
                if (propertySchemaObject.MinItems != 0 && count < propertySchemaObject.MinItems)
                {
                    AddArrayMinLengthError(testMessageResult, kv.Key, propertySchemaObject.MinItems);
                }

                if (propertySchemaObject.MaxItems != 0 && count > propertySchemaObject.MaxItems)
                {
                    AddArrayMaxLengthError(testMessageResult, kv.Key, propertySchemaObject.MaxItems);
                }

                var dataType = itemSchema.GetDataType();

                if (dataType == DataType.String)
                {
                    TryParseStringArray(propertySchemaObject, kv, itemArray, testMessageResult, itemSchema.Reference == "Guid");
                }

                if (dataType == DataType.Integer)
                {
                    TryParseIntegerArray(propertySchemaObject, kv, itemArray, testMessageResult);
                }

                if (itemSchema.Type.EqualsCaseInsensitive(DataType.Number.Value))
                {
                    TryParseNumberArray(propertySchemaObject, kv, itemArray, testMessageResult);
                }
            }
        }

        private void CheckObjectType(SchemaObject propertySchemaObject, KeyValuePair<string, object> kv,
            TestMessageResult testMessageResult)
        {
            CaseInsensitiveDictionary<object> innerObject = null;
            try
            {
                var json = JsonConvert.SerializeObject(kv.Value);
                innerObject = JsonConvert.DeserializeObject<CaseInsensitiveDictionary<object>>(json);
            }
            catch (Exception)
            {
                AddTypeError(testMessageResult, kv.Value.ToString(), kv.Key, "Object");
            }
            if (propertySchemaObject.Properties != null)
            {
                foreach (var innerProperty in propertySchemaObject.Properties)
                {
                    if (innerObject != null && innerObject.ContainsKey(innerProperty.Key))
                    {
                        ChecksForMessage(innerProperty.Value,
                            new KeyValuePair<string, object>($"{kv.Key}-->{innerProperty.Key}",
                                innerObject[innerProperty.Key]), testMessageResult);
                    }
                    else
                    {
                        AddNotFoundError(testMessageResult, $"{kv.Key}-->{innerProperty.Key}");
                    }
                }
            }
        }

        private static void AddTypeError(TestMessageResult result, string value, string key, string type)
        {
            result.IsMessageValid = false;
            result.MessageErrors.Add(
                $"\"{value}\" does not match the required data type for {key} ({type}).");

        }

        private static void AddFormatError(TestMessageResult result, string value, string key, string type)
        {
            result.IsMessageValid = false;
            result.MessageErrors.Add(
                $"\"{value}\" does not match the required format for {key} ({type}).");
        }

        private static void AddNotFoundError(TestMessageResult result, string property)
        {
            result.IsMessageValid = false;
            result.MessageErrors.Add(
                $"The value for field \"{property}\" was not found.");
        }

        private static void AddSchemaNotFoundError(TestMessageResult result, string property)
        {
            result.IsMessageValid = false;
            result.MessageErrors.Add(
                $"The schema for \"{property}\" was not found in the contract definition.");
        }

        private static void AddRequiredError(TestMessageResult result, string key, string type, string requiredProperty)
        {
            result.IsMessageValid = false;
            result.MessageErrors.Add(
                $"\"{key}\" does not have the required property({requiredProperty}) for type({type}).");
        }

        private static void AddItemTypeError(TestMessageResult result, string key, string type)
        {
            result.IsMessageValid = false;
            result.MessageErrors.Add(
                $"An item in the Items array for {key} does not match the required data type ({type}).");
        }

        private static void AddArrayMinLengthError(TestMessageResult result, string key, int min)
        {
            result.IsMessageValid = false;
            result.MessageErrors.Add(
                $"The Items array for {key} does not have the minimum number ({min}) of items required.");
        }

        private static void AddArrayMaxLengthError(TestMessageResult result, string key, int max)
        {
            result.IsMessageValid = false;
            result.MessageErrors.Add(
                $"The Items array for {key} has greater than the maximum number ({max}) of items allowed.");
        }

        private static void TryParseStringArray(SchemaObject propertySchemaObject, KeyValuePair<string, object> kv, dynamic itemArray, TestMessageResult testMessageResult, bool itemIsGuid)
        {
            var itemFormat = propertySchemaObject.Items.Format;

            if (itemFormat.EqualsCaseInsensitive(Format.DateTime.Value))
            {
                foreach (var item in itemArray)
                {
                    if (!DateTime.TryParse(item.ToString(), out DateTime _))
                    {
                        AddItemTypeError(testMessageResult, kv.Key, Format.DateTime.Value);
                        break;
                    }
                }
            }
            else if (itemIsGuid)
            {
                foreach (var item in itemArray)
                {
                    if (!Guid.TryParse(item.ToString(), out Guid _))
                    {
                        AddItemTypeError(testMessageResult, kv.Key, Format.Guid.DisplayName);
                        break;
                    }
                }
            }
        }

        private static void TryParseIntegerArray(SchemaObject propertySchemaObject, KeyValuePair<string, object> kv, dynamic itemArray, TestMessageResult testMessageResult)
        {
            var itemFormat = propertySchemaObject.Items.Format;

            foreach (var item in itemArray)
            {
                var isInt32 = int.TryParse(item.ToString(), out int _);
                var isInt64 = long.TryParse(item.ToString(), out long _);

                // Validate integer data type, which is int32.
                if (itemFormat == null && !isInt32)
                {
                    AddItemTypeError(testMessageResult, kv.Key, DataType.Integer.Value);
                    break;
                }

                if (itemFormat != null)
                {
                    // Validate specific integer formats
                    if (itemFormat.EqualsCaseInsensitive(Format.Int32.Value) && !isInt32)
                    {
                        AddItemTypeError(testMessageResult, kv.Key, Format.Int32.Value);
                        break;
                    }

                    if (itemFormat.EqualsCaseInsensitive(Format.Int64.Value) && !isInt64)
                    {
                        AddItemTypeError(testMessageResult, kv.Key, Format.Int64.Value);
                        break;
                    }
                }
            }
        }

        private static void TryParseNumberArray(SchemaObject propertySchemaObject, KeyValuePair<string, object> kv, dynamic itemArray, TestMessageResult testMessageResult)
        {
            var itemFormat = propertySchemaObject.Items.Format;

            foreach (var item in itemArray)
            {
                var isFloat = float.TryParse(item.ToString(), out float _);
                var isDouble = double.TryParse(item.ToString(), out double _);

                // Validate integer data type, which is int32.
                if (itemFormat == null && !isFloat)
                {
                    AddItemTypeError(testMessageResult, kv.Key, DataType.Number.Value);
                    break;
                }

                if (itemFormat != null)
                {
                    // Validate specific integer formats
                    if (itemFormat.EqualsCaseInsensitive(Format.Float.Value) && !isFloat)
                    {
                        AddItemTypeError(testMessageResult, kv.Key, Format.Float.Value);
                        break;
                    }

                    if (itemFormat.EqualsCaseInsensitive(Format.Double.Value) && !isDouble)
                    {
                        AddItemTypeError(testMessageResult, kv.Key, Format.Double.Value);
                        break;
                    }
                }
            }
        }

        public bool TryParseJSON(string json, out JsonValue jsonObject)
        {
            try
            {
                jsonObject = JsonValue.Parse(json);
                return true;
            }
            catch
            {
                jsonObject = null;
                return false;
            }
        }

        public class TestMessageResult
        {
            public bool IsMessageValid { get; set; } = true;
            public List<string> MessageErrors { get; set; } = new List<string>();
        }
    }
}
