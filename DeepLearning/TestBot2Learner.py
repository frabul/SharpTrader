import os
import json
import numpy as np
from random import shuffle
import keras
from keras.models import Sequential
from keras.layers import Dense, Dropout, Activation, Flatten
from keras.layers import Conv2D,Conv3D,MaxPooling3D, MaxPooling2D
from keras.callbacks import Callback
import msvcrt as ms

def printStats(predicted):
    stats = { 'rPos' : 0 , 'wPos' :0, 'neg' :0, 'won':0}
    y = predicted 
    length = 0 
    if  type(y) is np.ndarray:
        length = y.shape[0]
    else:
        length = len(y) 
    for i in range(length):
        if y[i][1] > y[i][0]: #predict buy
            stats['won'] += (wonTest[i] - 0.002)
            if yTest[i][1] > yTest[i][0]:
                stats['rPos'] += 1
            else:
                stats['wPos'] += 1
        else:
            stats['neg'] += 1
            
    profitFactor = 0
    if stats['wPos'] == 0 :
        stats['wPos'] += 1
    profitFactor = stats['rPos']/ stats['wPos'] 
    acc = stats['rPos'] / (stats['rPos'] + stats['wPos'] )
    print(f"Positives count: {stats['rPos']} - Profit factor: {profitFactor}"  
             + f" - Accuracy {acc} - Won: {stats['won']}")

def CheckKeyPress( comm ):
    keys = ""
    if ms.kbhit():
        while ms.kbhit():
            keys += ms.getwch()
        text = input(f"You pressed a key - write line {comm} to confirm")
        if text == comm:
            return True
        else:
            return False
 
class NBatchLogger(Callback):
    def __init__(self,display=100):
        '''
        display: Number of batches to wait before outputting loss
        '''
        self.seen = 0
        self.display = display 
    def on_epoch_end(self, epoch, logs={}):
        self.seen += 1
        if self.seen % self.display == 0:
            if CheckKeyPress('stop'):
                self.model.stop_training = True
            totEpochos = self.params['epochs']
            outStr = f'\nEpoch {self.seen}/{totEpochos} - '
            for k,v in logs.items():
                outStr += (str(k)+ ": " + str(v)+ "  ")
            print(outStr)
            printStats(model.predict(xTest))
 
#-----------------------------------------------
save_dir = "./"
model_name = "MyFirstModel"

fs = open("d:\\dataset.json", "r")  
data = json.load(fs )
#shuffle(data)
featuresList = []
labelsList = []
for d in data: 
    featuresList.append(d['Features'])
    labelsList.append(d['Labels']) 

sampleNum = len(featuresList)
trainLen = int( len(featuresList) * 4 / 5) 
xTrain = np.array(featuresList[0:trainLen ])
xTest = np.array(featuresList[trainLen: ])

#labelse have shape [sample, result, won]
#we need two arrays [sample, result] [sample, won]
#labelse have shape [sample, result, won]
#we need two arrays [sample, result] [sample, won] 
yTrain = [labelsList[i][0] for i in range(trainLen)]
wonTrain = [labelsList[i][1] for i in range(trainLen)]
yTest = [labelsList[i][0] for i in range(trainLen, sampleNum)]
wonTest = [labelsList[i][1] for i in range(trainLen, sampleNum)]
yTrain = np.array(yTrain)
yTest = np.array(yTest) 
   
xTrain = xTrain.astype('float32')
xTest = xTest.astype('float32')
yTrain = yTrain.astype('float32')
ytest = yTest.astype('float32')
yTrain = keras.utils.to_categorical(yTrain,2).astype('float32')
yTest = keras.utils.to_categorical(yTest,2).astype('float32')
 
printStats([[0,1] for i in range(yTest.shape[0])])
#------------------
model = Sequential()
model.add(Conv2D(32, (1, 3), padding='same',
                 input_shape=xTrain.shape[1:]))
model.add(Activation('relu'))
model.add(Conv2D(32, (3, 3)))
model.add(Activation('relu'))
model.add(Dropout(0.1))
model.add(Flatten())
model.add(Dense(128))
model.add(Activation('relu'))
model.add(Dropout(0.1))
model.add(Dense(2))
model.add(Activation('softmax'))

#----- INIT OPTIMIZER
#opt = keras.optimizers.rmsprop(lr=0.0001, decay=1e-6)
opt = keras.optimizers.Adam(lr=0.00028, decay=1e-6)
 
model.compile(loss='categorical_crossentropy',
              optimizer=opt,
              metrics=['accuracy'])
###### TRAIN #####
model.fit(xTrain, yTrain,
          batch_size=xTrain.shape[0],
          epochs=50000,
          validation_data=(xTest, yTest),
          shuffle=True,
          verbose=0,
          callbacks=[NBatchLogger(25),]
          )

# Save model and weights
if not os.path.isdir("./"):
    os.makedirs("./")
model_path = os.path.join(save_dir, model_name)
model.save(model_path)
print('Saved trained model at %s ' % model_path)

# Score trained model.
scores = model.evaluate(xTest, yTest, verbose=1)
print('Test loss:', scores[0])
print('Test accuracy:', scores[1])


 