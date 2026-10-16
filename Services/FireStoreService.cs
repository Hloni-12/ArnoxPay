using Google.Cloud.Firestore;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;

namespace ArnoxPay.Services
{
    public class FirestoreService
    {
        private readonly FirestoreDb _database;

        public FirestoreService()
        {
            _database = FirestoreDb.Create("arnoxpay");
        }

        public CollectionReference GetCollection(string name)
        {
            return _database.Collection(name);
        }

        public async Task<string> AddDocumentAsync(
            string collectionName,
            Dictionary<string, object> data)
        {
            DocumentReference document = await _database
                .Collection(collectionName)
                .AddAsync(data);

            return document.Id;
        }

        public async Task<DocumentSnapshot> GetDocumentAsync(
            string collectionName,
            string documentId)
        {
            return await _database
                .Collection(collectionName)
                .Document(documentId)
                .GetSnapshotAsync();
        }

        public async Task<List<DocumentSnapshot>> GetDocumentsAsync(
            string collectionName)
        {
            QuerySnapshot snapshot = await _database
                .Collection(collectionName)
                .GetSnapshotAsync();

            return snapshot.Documents.ToList();
        }

        public async Task UpdateDocumentAsync(
            string collectionName,
            string documentId,
            Dictionary<string, object> data)
        {
            await _database
                .Collection(collectionName)
                .Document(documentId)
                .SetAsync(data, SetOptions.MergeAll);
        }

        public async Task DeleteDocumentAsync(
            string collectionName,
            string documentId)
        {
            await _database
                .Collection(collectionName)
                .Document(documentId)
                .DeleteAsync();
        }
    }
}