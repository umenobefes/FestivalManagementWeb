using FestivalManagementWeb.Models;
using MongoDB.Driver;
using MongoDB.Bson;
using MongoDB.Driver.GridFS;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace FestivalManagementWeb.Repositories
{
    public class ImageKeyValueRepository : IImageKeyValueRepository
    {
        private readonly IMongoCollection<ImageKeyValue> _imageCollection;
        private readonly IGridFSBucket _gridFsBucket;

        public ImageKeyValueRepository(IMongoDatabase database, IGridFSBucket gridFsBucket)
        {
            _imageCollection = database.GetCollection<ImageKeyValue>("ImageKeyValues");
            _gridFsBucket = gridFsBucket;
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

        public async Task<long> DeleteByYearAsync(int year)
        {
            var filter = Builders<ImageKeyValue>.Filter.Eq(x => x.Year, year);
            var items = await _imageCollection.Find(filter).ToListAsync();

            foreach (var item in items)
            {
                if (item.GridFSFileId != ObjectId.Empty)
                {
                    try
                    {
                        await _gridFsBucket.DeleteAsync(item.GridFSFileId);
                    }
                    catch (GridFSFileNotFoundException)
                    {
                        // ignore missing blobs
                    }
                }
            }

            var result = await _imageCollection.DeleteManyAsync(filter);
            return result.IsAcknowledged ? result.DeletedCount : 0;
        }

        public async Task<IEnumerable<ImageKeyValue>> GetAllAsync(int year)
        {
            return await _imageCollection.Find(x => x.Year == year).ToListAsync();
        }

        public async Task<ImageKeyValue> GetByIdAsync(Guid id)
        {
            return await _imageCollection.Find(x => x.Id == id).FirstOrDefaultAsync();
        }

        public async Task<ImageKeyValue> GetByKeyAsync(string key, int year)
        {
            return await _imageCollection.Find(x => x.Key == key && x.Year == year).FirstOrDefaultAsync();
        }

        public async Task<IEnumerable<ImageKeyValue>> GetDeployedBeforeAsync(DateTime date, int year)
        {
            return await _imageCollection.Find(x => x.Year == year && x.DeployedDate < date).ToListAsync();
        }

        public async Task<bool> UpdateAsync(ImageKeyValue imageKeyValue)
        {
            var result = await _imageCollection.ReplaceOneAsync(x => x.Id == imageKeyValue.Id, imageKeyValue);
            return result.IsAcknowledged && result.ModifiedCount > 0;
        }

        public async Task<IReadOnlyList<int>> GetDistinctYearsAsync()
        {
            var cursor = await _imageCollection.DistinctAsync(x => x.Year, FilterDefinition<ImageKeyValue>.Empty);
            var years = await cursor.ToListAsync();
            return years;
        }
    }
}
