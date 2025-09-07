using FestivalManagementWeb.Models;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace FestivalManagementWeb.Repositories
{
    public class ImageKeyValueRepository : IImageKeyValueRepository
    {
        private readonly IMongoCollection<ImageKeyValue> _imageCollection;

        public ImageKeyValueRepository(IMongoDatabase database)
        {
            _imageCollection = database.GetCollection<ImageKeyValue>("ImageKeyValues");
        }

        public async Task CreateAsync(ImageKeyValue imageKeyValue)
        {
            await _imageCollection.InsertOneAsync(imageKeyValue);
        }

        public async Task<bool> DeleteAsync(Guid id)
        {
            var result = await _imageCollection.DeleteOneAsync(x => x.Id == id);
            return result.IsAcknowledged && result.DeletedCount > 0;
        }

        public async Task<IEnumerable<ImageKeyValue>> GetAllAsync()
        {
            return await _imageCollection.Find(_ => true).ToListAsync();
        }

        public async Task<ImageKeyValue> GetByIdAsync(Guid id)
        {
            return await _imageCollection.Find(x => x.Id == id).FirstOrDefaultAsync();
        }

        public async Task<ImageKeyValue> GetByKeyAsync(string key)
        {
            return await _imageCollection.Find(x => x.Key == key).FirstOrDefaultAsync();
        }

        public async Task<IEnumerable<ImageKeyValue>> GetDeployedBeforeAsync(DateTime date)
        {
            return await _imageCollection.Find(x => x.DeployedDate < date).ToListAsync();
        }

        public async Task<bool> UpdateAsync(ImageKeyValue imageKeyValue)
        {
            var result = await _imageCollection.ReplaceOneAsync(x => x.Id == imageKeyValue.Id, imageKeyValue);
            return result.IsAcknowledged && result.ModifiedCount > 0;
        }
    }
}
