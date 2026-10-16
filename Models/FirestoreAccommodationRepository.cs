using ArnoxPay.Models;
using ArnoxPay.Services;
using Google.Cloud.Firestore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ArnoxPay.Repositories
{
    public class FirestoreAccommodationRepository
    {
        private readonly FirestoreService _firestoreService;
        private const string CollectionName = "accomodations";

        public FirestoreAccommodationRepository(
            FirestoreService firestoreService)
        {
            _firestoreService = firestoreService;
        }

        public async Task<List<Accomodation>> GetAllAsync()
        {
            List<DocumentSnapshot> documents =
                await _firestoreService.GetDocumentsAsync(CollectionName);

            List<Accomodation> accommodations =
                new List<Accomodation>();

            foreach (DocumentSnapshot document in documents)
            {
                Accomodation accommodation = new Accomodation();

                accommodation.FirestoreId = document.Id;

                accommodation.Name =
                    document.ContainsField("Name")
                        ? document.GetValue<string>("Name")
                        : document.ContainsField("name")
                            ? document.GetValue<string>("name")
                            : string.Empty;

                accommodation.UnitNumber =
                    document.ContainsField("UnitNumber")
                        ? document.GetValue<string>("UnitNumber")
                        : string.Empty;

                accommodation.Address =
                    document.ContainsField("Address")
                        ? document.GetValue<string>("Address")
                        : document.ContainsField("location")
                            ? document.GetValue<string>("location")
                            : string.Empty;

                accommodation.Description =
                    document.ContainsField("Description")
                        ? document.GetValue<string>("Description")
                        : document.ContainsField("description")
                            ? document.GetValue<string>("description")
                            : null;

                accommodation.IsActive =
                    document.ContainsField("IsActive")
                        ? document.GetValue<bool>("IsActive")
                        : document.ContainsField("Available")
                            ? document.GetValue<bool>("Available")
                            : true;

                if (document.ContainsField("Capacity"))
                {
                    accommodation.Capacity =
                        Convert.ToInt32(document.GetValue<long>("Capacity"));
                }

                if (document.ContainsField("AccommodationType"))
                {
                    string type =
                        document.GetValue<string>("AccommodationType");

                    Enum.TryParse(type, true,
                        out AccommodationType accommodationType);

                    accommodation.AccommodationType = accommodationType;
                }

                accommodations.Add(accommodation);
            }

            return accommodations;
        }

        public async Task<Accomodation?> GetByIdAsync(string id)
        {
            DocumentSnapshot document =
                await _firestoreService.GetDocumentAsync(
                    CollectionName, id);

            if (!document.Exists)
            {
                return null;
            }

            List<Accomodation> accommodations =
                await GetAllAsync();

            return accommodations.FirstOrDefault(
                accommodation => accommodation.FirestoreId == id);
        }

        public async Task<string> AddAsync(Accomodation accommodation)
        {
            Dictionary<string, object> data =
                new Dictionary<string, object>
                {
                    { "Name", accommodation.Name },
                    { "UnitNumber", accommodation.UnitNumber },
                    { "Address", accommodation.Address },
                    { "AccommodationType",
                        accommodation.AccommodationType.ToString() },
                    { "Capacity", accommodation.Capacity },
                    { "Description",
                        accommodation.Description ?? string.Empty },
                    { "IsActive", accommodation.IsActive }
                };

            string id = await _firestoreService.AddDocumentAsync(
                CollectionName, data);

            accommodation.FirestoreId = id;

            return id;
        }

        public async Task UpdateAsync(Accomodation accommodation)
        {
            if (string.IsNullOrWhiteSpace(accommodation.FirestoreId))
            {
                throw new ArgumentException(
                    "Firestore accommodation ID is required.");
            }

            Dictionary<string, object> data =
                new Dictionary<string, object>
                {
                    { "Name", accommodation.Name },
                    { "UnitNumber", accommodation.UnitNumber },
                    { "Address", accommodation.Address },
                    { "AccommodationType",
                        accommodation.AccommodationType.ToString() },
                    { "Capacity", accommodation.Capacity },
                    { "Description",
                        accommodation.Description ?? string.Empty },
                    { "IsActive", accommodation.IsActive }
                };

            await _firestoreService.UpdateDocumentAsync(
                CollectionName, accommodation.FirestoreId, data);
        }

        public async Task DeleteAsync(string id)
        {
            await _firestoreService.DeleteDocumentAsync(
                CollectionName, id);
        }
    }
}
