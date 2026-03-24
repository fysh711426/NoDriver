using NoDriver.Core.Messaging;
using NoDriver.Core.Runtime;
using System.Text.Json.Nodes;

namespace Test
{
    [TestClass]
    public class TransactionTests
    {
        public class DummyParams
        {
            public string Data { get; set; } = "";
        }

        [TestMethod]
        public void Constructor_ShouldInitializesPropertiesCorrectly()
        {
            // Arrange
            var id = 123;
            var method = "Test.Method";
            var @params = new DummyParams { Data = "Hello" };

            // Act
            var tx = new Transaction<DummyParams>(id, method, @params);

            // Assert
            Assert.AreEqual(id, tx.Id, "Id 應與傳入的參數一致");
            Assert.AreEqual(method, tx.Method, "Method 應與傳入的參數一致");
            Assert.AreEqual(@params, tx.Params, "Params 應正確引用傳入的實例");
            Assert.IsNotNull(tx.Task, "應自動建立相關的 Task");
            Assert.IsFalse(tx.Task.IsCompleted, "Task 狀態不應該是已完成");
            Assert.IsFalse(tx.HasException, "HasException 應為 false");
        }

        [TestMethod]
        public void Message_ShouldReturnsSerializedJsonString()
        {
            // Arrange
            var tx = new Transaction<DummyParams>(1, "Network.enable", new());

            // Act
            var message = tx.Message;

            // Assert
            Assert.IsFalse(string.IsNullOrWhiteSpace(message), "不應為空字串");
            Assert.IsTrue(message.Contains(@"""id"":1"), "應包含正確的序列化 Id");
            Assert.IsTrue(message.Contains("Network.enable"), "應包含正確的 Method 名稱");
        }

        [TestMethod]
        public async Task ProcessResponse_WithSuccess_ShouldSetsTaskResult()
        {
            // Arrange
            var tx = new Transaction<DummyParams>(1, "Test", new());
            
            var response = new ProtocolResponse
            {
                Error = null,
                Result = new JsonObject 
                { 
                    ["status"] = "ok" 
                }
            };

            // Act
            tx.ProcessResponse(response);
            var result = await tx.Task;

            // Assert
            Assert.IsFalse(tx.HasException, "收到成功的回應時，HasException 應被設定為 false");
            Assert.IsTrue(tx.Task.IsCompletedSuccessfully, "收到成功的回應時，應要進入 Completed 狀態");
            Assert.IsTrue(result?["status"]?.ToString() == "ok", "Result 中的內容與原始回應不符");
        }

        [TestMethod]
        public async Task ProcessResponse_WithError_ShouldSetsTaskException()
        {
            // Arrange
            var tx = new Transaction<DummyParams>(1, "Test", new());

            var response = new ProtocolResponse
            {
                Result = null,
                Error = new ProtocolErrorInfo
                {
                    Code = -32000,
                    Message = "Server error"
                }
            };

            // Act
            tx.ProcessResponse(response);

            // Assert
            Assert.IsTrue(tx.HasException, "收到錯誤回應時，HasException 應被設定為 true");
            Assert.IsTrue(tx.Task.IsFaulted, "收到錯誤回應時，狀態應變更為 Faulted");

            // 驗證 Task 是否拋出 ProtocolErrorException
            await Assert.ThrowsExceptionAsync<ProtocolErrorException>(async () => await tx.Task);
        }

        [TestMethod]
        public async Task Cancel_ShouldSetsTaskException()
        {
            // Arrange
            var tx = new Transaction<DummyParams>(1, "Test", new());

            // Act
            tx.Cancel(new InvalidOperationException("Force canceled"));

            // Assert
            Assert.IsTrue(tx.HasException, "被取消時，HasException 應被設定為 true");
            Assert.IsTrue(tx.Task.IsFaulted, "被取消時，狀態應變更為 Faulted");

            var ex = await Assert.ThrowsExceptionAsync<InvalidOperationException>(async () => await tx.Task);
            Assert.AreEqual("Force canceled", ex.Message, "異常訊息應與調用 Cancel 時傳入的訊息一致");
        }

        [TestMethod]
        public void ToString_PendingState_ShouldReturnsCorrectFormat()
        {
            // Arrange
            var tx = new Transaction<DummyParams>(1, "Test.Method", new());

            // Act
            var result = tx.ToString();

            // Assert
            Assert.IsTrue(result.Contains("DummyParams"), "應包含泛型參數型別");
            Assert.IsTrue(result.Contains("Test.Method"), "應包含 Method 名稱");
            Assert.IsTrue(result.Contains("Status: Pending"), "Status 應顯示為 Pending");
            Assert.IsTrue(result.Contains("Success: False"), "Success 應顯示為 False");
        }

        [TestMethod]
        public void ToString_FinishedSuccessState_ShouldReturnsCorrectFormat()
        {
            // Arrange
            var tx = new Transaction<DummyParams>(1, "Test.Method", new());

            var response = new ProtocolResponse
            {
                Error = null,
                Result = new JsonObject()
            };

            // Act
            tx.ProcessResponse(response);
            var result = tx.ToString();

            // Assert
            Assert.IsTrue(result.Contains("Status: Finished"), "Status 應顯示為 Finished");
            Assert.IsTrue(result.Contains("Success: True"), "Success 應顯示為 True");
        }

        [TestMethod]
        public void ToString_FinishedErrorState_ShouldReturnsCorrectFormat()
        {
            // Arrange
            var tx = new Transaction<DummyParams>(1, "Test.Method", new());

            // Act
            tx.Cancel(new Exception("Fail"));
            var result = tx.ToString();

            // Assert
            Assert.IsTrue(result.Contains("Status: Finished"), "Status 應顯示為 Finished");
            Assert.IsTrue(result.Contains("Success: False"), "Success 應顯示為 False");
        }
    }
}
